//
// Copyright Fela Ameghino 2015-2024
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Controls;
using Telegram.Navigation;
using Telegram.Navigation.Services;
using Telegram.Services;
using Telegram.Services.ViewService;
using Telegram.Streams;
using Telegram.Td.Api;
using Telegram.ViewModels;
using Telegram.ViewModels.Payments;
using Telegram.ViewModels.Settings;
using Telegram.Views;
using Telegram.Views.Payments;
using Telegram.Views.Premium.Popups;
using Telegram.Views.Settings;
using Telegram.Views.Settings.Password;
using Telegram.Views.Settings.Popups;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace Telegram.Common
{
    public class TLNavigationService : NavigationService
    {
        private readonly IClientService _clientService;
        private readonly IPasscodeService _passcodeService;
        private readonly IViewService _viewService;

        private readonly Dictionary<string, AppWindow> _instantWindows = new Dictionary<string, AppWindow>();

        public TLNavigationService(IClientService clientService, IViewService viewService, Frame frame, int session, string id)
            : base(frame, session, id)
        {
            _clientService = clientService;
            _passcodeService = TypeResolver.Current.Passcode;
            _viewService = viewService;
        }

        public IClientService ClientService => _clientService;

        public async void NavigateToInstant(string url, string fallbackUrl = null)
        {
            var response = await ClientService.SendAsync(new GetWebPageInstantView(url, true));
            if (response is WebPageInstantView instantView)
            {
                Navigate(typeof(InstantPage), new InstantPageArgs(instantView, url));
            }
            else
            {
                if (Uri.TryCreate(fallbackUrl ?? url, UriKind.Absolute, out Uri uri))
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
            }
        }

        public async void ShowLimitReached(PremiumLimitType type)
        {
            await new LimitReachedPopup(this, _clientService, type).ShowQueuedAsync();
        }

        public async void ShowPromo(PremiumSource source = null)
        {
            await ShowPopupAsync(typeof(PromoPopup), source);
        }

        public Task ShowPromoAsync(PremiumSource source = null, ElementTheme requestedTheme = ElementTheme.Default)
        {
            return ShowPopupAsync(typeof(PromoPopup), source, requestedTheme: requestedTheme);
        }

        public async void NavigateToInvoice(MessageViewModel message)
        {
            var parameters = new ViewServiceParams
            {
                Title = message.Content is MessageInvoice { ReceiptMessageId: 0 } ? Strings.PaymentCheckout : Strings.PaymentReceipt,
                Width = 380,
                Height = 580,
                PersistentId = "Payments",
                Content = control =>
                {
                    var nav = BootStrapper.Current.NavigationServiceFactory(BootStrapper.BackButton.Ignore, SessionId, "Payments" + Guid.NewGuid(), false);
                    nav.Navigate(typeof(PaymentFormPage), new InputInvoiceMessage(message.ChatId, message.Id));

                    return BootStrapper.Current.CreateRootElement(nav);
                }
            };

            await _viewService.OpenAsync(parameters);
        }

        public async void NavigateToInvoice(InputInvoice inputInvoice)
        {
            var response = await ClientService.SendAsync(new GetPaymentForm(inputInvoice, Theme.Current.Parameters));
            if (response is not PaymentForm paymentForm)
            {
                ToastPopup.Show(Strings.PaymentInvoiceLinkInvalid, new LocalFileSource("ms-appx:///Assets/Toasts/Info.tgs"));
                return;
            }

            var parameters = new ViewServiceParams
            {
                Title = Strings.PaymentCheckout,
                Width = 380,
                Height = 580,
                PersistentId = "Payments",
                Content = control =>
                {
                    var nav = BootStrapper.Current.NavigationServiceFactory(BootStrapper.BackButton.Ignore, SessionId, "Payments" + Guid.NewGuid(), false);
                    nav.Navigate(typeof(PaymentFormPage), new PaymentFormArgs(inputInvoice, paymentForm));

                    return BootStrapper.Current.CreateRootElement(nav);

                }
            };

            await _viewService.OpenAsync(parameters);
        }

        public void NavigateToSender(MessageSender sender)
        {
            if (sender is MessageSenderUser user)
            {
                NavigateToUser(user.UserId, false);
            }
            else if (sender is MessageSenderChat chat)
            {
                Navigate(typeof(ProfilePage), chat.ChatId);
            }
        }

        public async void NavigateToChat(Chat chat, long? message = null, long? thread = null, SavedMessagesTopic? topic = null, string accessToken = null, NavigationState state = null, bool scheduled = false, bool force = true, bool createNewWindow = false, bool clearBackStack = false)
        {
            if (Dispatcher.HasThreadAccess is false)
            {
                // This should not happen but it currently does when scheduling a file
                Logger.Error(Environment.StackTrace);

                Dispatcher.Dispatch(() => NavigateToChat(chat, message, thread, topic, accessToken, state, scheduled, force, createNewWindow, clearBackStack));
                return;
            }

            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate privata)
            {
                var user = _clientService.GetUser(privata.UserId);
                if (user == null)
                {
                    return;
                }

                if (user.Id == _clientService.Options.MyId && topic == null)
                {
                    var settings = TypeResolver.Current.Resolve<ISettingsService>(_clientService.SessionId);
                    if (settings != null && settings.SavedViewAsChats)
                    {
                        Navigate(typeof(ProfilePage), chat.Id);
                        return;
                    }
                }

                if (user.RestrictionReason.Length > 0)
                {
                    await MessagePopup.ShowAsync(user.RestrictionReason, Strings.AppName, Strings.OK);
                    return;
                }
                else if (user.Id == _clientService.Options.AntiSpamBotUserId)
                {
                    var groupInfo = Strings.EventLogFilterGroupInfo;
                    var administrators = Strings.ChannelAdministrators;
                    var path = $"{groupInfo} > {administrators}";

                    var text = string.Format(Strings.ChannelAntiSpamInfo2, path);
                    var index = Strings.ChannelAntiSpamInfo2.IndexOf("{0}");

                    var formatted = new FormattedText(text, new[] { new TextEntity(index, path.Length, new TextEntityTypeTextUrl("tg://")) });

                    await MessagePopup.ShowAsync(formatted, Strings.AppName, Strings.OK);
                    return;
                }
            }
            else if (chat.Type is ChatTypeSupergroup super)
            {
                var supergroup = _clientService.GetSupergroup(super.SupergroupId);
                if (supergroup == null)
                {
                    return;
                }

                if (supergroup.Status is ChatMemberStatusLeft && !supergroup.IsPublic() && !_clientService.IsChatAccessible(chat))
                {
                    await MessagePopup.ShowAsync(Strings.ChannelCantOpenPrivate, Strings.AppName, Strings.OK);
                    return;
                }

                if (supergroup.RestrictionReason.Length > 0)
                {
                    await MessagePopup.ShowAsync(supergroup.RestrictionReason, Strings.AppName, Strings.OK);
                    return;
                }
            }

            // TODO: do current page matching for ChatSavedPage and ChatThreadPage as well.
            if (Frame.Content is ChatPage page && page.ViewModel != null && chat.Id.Equals((long)CurrentPageParam) && thread == null && topic == null && !scheduled && !createNewWindow)
            {
                var viewModel = page.ViewModel;
                if (message != null)
                {
                    await viewModel.LoadMessageSliceAsync(null, message.Value);
                }
                else
                {
                    await viewModel.LoadLastSliceAsync();
                }

                if (viewModel != page.ViewModel)
                {
                    return;
                }

                if (accessToken != null && ClientService.TryGetUser(chat, out User user) && ClientService.TryGetUserFull(chat, out UserFullInfo userFull))
                {
                    page.ViewModel.AccessToken = accessToken;
                    page.View.UpdateUserFullInfo(chat, user, userFull, false, true);
                }

                page.ViewModel.TextField?.Focus(FocusState.Programmatic);

                if (App.DataPackages.TryRemove(chat.Id, out DataPackageView package1))
                {
                    await page.ViewModel.HandlePackageAsync(package1);
                }
                else if (state != null && state.TryGet("package", out DataPackageView package2))
                {
                    await page.ViewModel.HandlePackageAsync(package2);
                }

                OverlayWindow.Current?.TryHide(ContentDialogResult.None);
            }
            else
            {
                //NavigatedEventHandler handler = null;
                //handler = async (s, args) =>
                //{
                //    Frame.Navigated -= handler;

                //    if (args.Content is DialogPage page1 /*&& chat.Id.Equals((long)args.Parameter)*/)
                //    {
                //        if (message.HasValue)
                //        {
                //            await page1.ViewModel.LoadMessageSliceAsync(null, message.Value);
                //        }
                //    }
                //};

                //Frame.Navigated += handler;

                state ??= new NavigationState();

                if (message != null)
                {
                    state["message_id"] = message.Value;
                }

                if (accessToken != null)
                {
                    state["access_token"] = accessToken;
                }

                if (createNewWindow)
                {
                    Type target;
                    object parameter;

                    if (thread != null)
                    {
                        target = typeof(ChatThreadPage);
                        parameter = new ChatNavigationArgs(chat.Id, thread.Value);
                    }
                    else if (topic != null)
                    {
                        target = typeof(ChatSavedPage);
                        parameter = new ChatNavigationArgs(chat.Id, topic);
                    }
                    else if (scheduled)
                    {
                        target = typeof(ChatScheduledPage);
                        parameter = chat.Id;
                    }
                    else
                    {
                        target = typeof(ChatPage);
                        parameter = chat.Id;
                    }

                    // This is horrible here but I don't want to bloat this method with dozens of parameters.
                    var masterDetailPanel = Window.Current.Content.GetChild<MasterDetailPanel>();
                    if (masterDetailPanel != null)
                    {
                        await OpenAsync(target, parameter, size: new Windows.Foundation.Size(masterDetailPanel.ActualDetailWidth, masterDetailPanel.ActualHeight));
                    }
                    else
                    {
                        await OpenAsync(target, parameter);
                    }
                }
                else
                {
                    // TODO: do current page matching for ChatSavedPage and ChatThreadPage as well.
                    if (Frame.Content is ChatPage chatPage && thread == null && topic == null && !scheduled && !force)
                    {
                        chatPage.ViewModel.NavigatedFrom(null, false);

                        chatPage.Deactivate(true);
                        chatPage.Activate(SessionId);
                        chatPage.ViewModel.NavigationService = this;
                        chatPage.ViewModel.Dispatcher = Dispatcher;
                        await chatPage.ViewModel.NavigatedToAsync(chat.Id, Windows.UI.Xaml.Navigation.NavigationMode.New, state);

                        FrameFacade.RaiseNavigated(chat.Id);
                        Frame.ForwardStack.Clear();

                        if (clearBackStack)
                        {
                            GoBackAt(0, false);
                        }

                        OverlayWindow.Current?.TryHide(ContentDialogResult.None);
                    }
                    else
                    {
                        Type target;
                        NavigationTransitionInfo info = null;
                        object parameter;

                        if (thread != null)
                        {
                            target = typeof(ChatThreadPage);
                            parameter = new ChatNavigationArgs(chat.Id, thread.Value);

                            if (CurrentPageType == typeof(ChatPage) && chat.Id.Equals((long)CurrentPageParam))
                            {
                                info = new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight };
                            }
                            else
                            {
                                info = new SuppressNavigationTransitionInfo();
                            }
                        }
                        else if (topic != null)
                        {
                            target = typeof(ChatSavedPage);
                            parameter = new ChatNavigationArgs(chat.Id, topic);

                            if (CurrentPageType == typeof(ChatPage) && chat.Id.Equals((long)CurrentPageParam))
                            {
                                info = new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight };
                            }
                            else
                            {
                                info = new SuppressNavigationTransitionInfo();
                            }
                        }
                        else if (scheduled)
                        {
                            target = typeof(ChatScheduledPage);
                            parameter = chat.Id;
                        }
                        else
                        {
                            target = typeof(ChatPage);
                            parameter = chat.Id;

                            if (CurrentPageType == typeof(ProfilePage) && CurrentPageParam is long profileId && profileId == chat.Id)
                            {
                                info = new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft };
                            }
                        }

                        if (Navigate(target, parameter, state, info))
                        {
                            if (clearBackStack)
                            {
                                GoBackAt(0, false);
                            }
                        }
                    }
                }
            }
        }

        public async void NavigateToChat(long chatId, long? message = null, long? thread = null, SavedMessagesTopic? topic = null, string accessToken = null, NavigationState state = null, bool scheduled = false, bool force = true, bool createNewWindow = false)
        {
            var chat = _clientService.GetChat(chatId);

            // TODO: this should never happen
            chat ??= await _clientService.SendAsync(new GetChat(chatId)) as Chat;

            if (chat == null)
            {
                return;
            }

            NavigateToChat(chat, message, thread, topic, accessToken, state, scheduled, force, createNewWindow);
        }

        public async void NavigateToUser(long userId, bool toChat = false)
        {
            if (_clientService.TryGetChatFromUser(userId, out Chat chat))
            {
                var user = ClientService.GetUser(userId);
                if (user?.Type is UserTypeBot || toChat)
                {
                    NavigateToChat(chat);
                }
                else
                {
                    Navigate(typeof(ProfilePage), chat.Id);
                }
            }
            else
            {
                var response = await _clientService.SendAsync(new CreatePrivateChat(userId, false));
                if (response is Chat created)
                {
                    var user = ClientService.GetUser(userId);
                    if (user?.Type is UserTypeBot || toChat)
                    {
                        NavigateToChat(created);
                    }
                    else
                    {
                        Navigate(typeof(ProfilePage), created.Id);
                    }
                }
            }
        }

        public async void NavigateToPasscode()
        {
            if (_passcodeService.IsEnabled)
            {
                var popup = new SettingsPasscodeConfirmPopup();

                var confirm = await popup.ShowQueuedAsync();
                if (confirm == ContentDialogResult.Primary)
                {
                    Navigate(typeof(SettingsPasscodePage));
                }
            }
            else
            {
                var popup = new SettingsPasscodePopup();

                var confirm = await popup.ShowQueuedAsync();
                if (confirm == ContentDialogResult.Primary)
                {
                    var viewModel = TypeResolver.Current.Resolve<SettingsPasscodeViewModel>(SessionId);
                    if (viewModel != null && await viewModel.ToggleAsync())
                    {
                        Navigate(typeof(SettingsPasscodePage));
                    }
                }
            }
        }

        public async Task<PasswordState> NavigateToPasswordAsync()
        {
            var intro = new SettingsPasswordIntroPopup();

            if (ContentDialogResult.Primary != await intro.ShowQueuedAsync())
            {
                return null;
            }

            var password = new SettingsPasswordCreatePopup();

            if (ContentDialogResult.Primary != await password.ShowQueuedAsync())
            {
                return null;
            }

            var hint = new SettingsPasswordHintPopup(null, null, password.Password);

            if (ContentDialogResult.Primary != await hint.ShowQueuedAsync())
            {
                return null;
            }

            var emailAddress = new SettingsPasswordEmailAddressPopup(ClientService, new SetPassword(string.Empty, password.Password, hint.Hint, true, string.Empty));

            if (ContentDialogResult.Primary != await emailAddress.ShowQueuedAsync())
            {
                return null;
            }

            PasswordState passwordState;

            if (emailAddress.PasswordState?.RecoveryEmailAddressCodeInfo != null)
            {
                var emailCode = new SettingsPasswordEmailCodePopup(ClientService, emailAddress.PasswordState?.RecoveryEmailAddressCodeInfo, SettingsPasswordEmailCodeType.New);

                if (ContentDialogResult.Primary != await emailCode.ShowQueuedAsync())
                {
                    return null;
                }

                passwordState = emailCode.PasswordState;
            }
            else
            {
                passwordState = emailAddress.PasswordState;
            }

            await new SettingsPasswordDonePopup().ShowQueuedAsync();
            return passwordState;
        }
    }
}
