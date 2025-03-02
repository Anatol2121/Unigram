//
// Copyright Fela Ameghino 2015-2024
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//
using System;
using System.Collections.Generic;
using Telegram.Common;
using Telegram.Services;
using Telegram.Td.Api;
using Telegram.ViewModels.Delegates;

namespace Telegram.ViewModels
{
    public class MessageViewModel : MessageWithOwner
    {
        private readonly IPlaybackService _playbackService;

        // TODO: find a way NOT to use a weak reference here
        private readonly WeakReference _delegate;

        protected readonly Chat _chat;

        private Action _updateSelection;

        public MessageViewModel(IClientService clientService, IPlaybackService playbackService, IMessageDelegate delegato, Chat chat, Message message, bool processText = false)
            : base(clientService, message)
        {
            _playbackService = playbackService;
            _delegate = new WeakReference(delegato);

            _chat = chat;

            if (processText)
            {
                SetText(message.Content?.GetCaption());
            }
        }

        public Chat Chat => _chat;

        public void SelectionChanged()
        {
            _updateSelection?.Invoke();
        }

        public void UpdateSelectionCallback(Action action)
        {
            _updateSelection = action;
        }

        //public void Cleanup()
        //{
        //    _playbackService = null;
        //    _delegate = null;

        //    _updateSelection = null;

        //    if (_message.Content is MessageAlbum album)
        //    {
        //        foreach (var child in album.Messages)
        //        {
        //            child.Cleanup();
        //        }

        //        album.Messages.Clear();
        //    }

        //    ReplyToMessage?.Cleanup();
        //    ReplyToMessage = null;
        //}

        public IPlaybackService PlaybackService => _playbackService;
        public IMessageDelegate Delegate => _delegate.Target as IMessageDelegate;

        public bool IsInitial { get; set; } = true;

        public bool IsFirst { get; set; } = true;
        public bool IsLast { get; set; } = true;

        // Used only by animated emojis
        public Sticker Interaction { get; set; }

        // Used only in recent actions
        public ChatEvent Event { get; set; }

        public Photo GetPhoto() => _message.GetPhoto();

        private bool? _isService;
        public bool IsService => _isService ??= _message.IsService();

        private bool? _isSaved;
        public bool IsSaved => _isSaved ??= _message.IsSaved(_clientService.Options.MyId);

        // TODO: BaseObject
        public object ReplyToItem { get; set; }
        public MessageReplyToState ReplyToState { get; set; } = MessageReplyToState.None;

        public void Reset()
        {
            _isService = null;
        }

        private MessageContent _generatedContent;

        /// <summary>
        /// This is used for additional content that's generated by the app
        /// </summary>
        public MessageContent GeneratedContent
        {
            get => _generatedContent;
            set
            {
                _generatedContent = value;

                if (value != null)
                {
                    SetText(value.GetCaption());
                }
            }
        }

        public bool GeneratedContentUnread { get; set; }

        public BaseObject GetSender()
        {
            if (_message.SenderId is MessageSenderUser user)
            {
                return ClientService.GetUser(user.UserId);
            }
            else if (_message.SenderId is MessageSenderChat chat)
            {
                return ClientService.GetChat(chat.ChatId);
            }

            return null;
        }

        public User GetViaBotUser()
        {
            if (_message.ViaBotUserId != 0)
            {
                return ClientService.GetUser(_message.ViaBotUserId);
            }

            if (ClientService.TryGetUser(_message.SenderId, out User user) && user.Type is UserTypeBot)
            {
                return user;
            }

            return null;
        }


        public override bool CanBeAddedToDownloads => CanBeSaved && !Chat.HasProtectedContent && Content is MessageAudio or MessageDocument or MessageVideo;

        public void Replace(Message message)
        {
            _message = message;
        }

        private bool? _canBeShared;
        public bool CanBeShared => _canBeShared ??= GetCanBeShared();

        private bool GetCanBeShared()
        {
            if (SchedulingState != null || !CanBeSaved)
            {
                return false;
            }
            //else if (eventId != 0)
            //{
            //    return false;
            //}
            else if (IsSaved)
            {
                return true;
            }
            else if (Content is MessageSticker or MessageDice)
            {
                return false;
            }
            else if (ForwardInfo?.Origin is MessageOriginChannel && !IsOutgoing)
            {
                return true;
            }
            else if (SenderId is MessageSenderUser senderUser)
            {
                if (Content is MessageText text && text.WebPage == null)
                {
                    return false;
                }

                if (!IsOutgoing)
                {
                    var user = ClientService.GetUser(senderUser.UserId);
                    if (user != null && user.Type is UserTypeBot)
                    {
                        return true;
                    }

                    if (Content is MessageGame or MessageInvoice)
                    {
                        return true;
                    }

                    if (Chat?.Type is ChatTypeSupergroup super && !super.IsChannel)
                    {
                        var supergroup = ClientService.GetSupergroup(super.SupergroupId);
                        return supergroup != null && supergroup.IsPublic() && Content is not MessageContact and not MessageLocation;
                    }
                }
            }
            else if (IsChannelPost)
            {
                if (ViaBotUserId == 0 && ReplyTo == null || Content is not MessageSticker)
                {
                    return true;
                }
            }

            return false;
        }

        private bool? _hasSenderPhoto;
        public bool HasSenderPhoto => _hasSenderPhoto ??= GetHasSenderPhoto();

        private bool GetHasSenderPhoto()
        {
            if (IsService)
            {
                return false;
            }

            if (IsChannelPost)
            {
                return false;
            }
            else if (IsSaved && ForwardInfo?.Source is { IsOutgoing: false })
            {
                return true;
            }
            else if (IsOutgoing)
            {
                return false;
            }

            return Chat?.Type is ChatTypeSupergroup or ChatTypeBasicGroup;
        }


        public int AnimationHash()
        {
            return base.GetHashCode();
        }

        public void UpdateWith(MessageViewModel message)
        {
            UpdateWith(message.Get());
        }

        public void UpdateWith(Message message)
        {
            _message.AuthorSignature = message.AuthorSignature;
            _message.CanBeDeletedForAllUsers = message.CanBeDeletedForAllUsers;
            _message.CanBeDeletedOnlyForSelf = message.CanBeDeletedOnlyForSelf;
            _message.CanBeEdited = message.CanBeEdited;
            _message.CanBeSaved = message.CanBeSaved;
            _message.CanBeForwarded = message.CanBeForwarded;
            _message.CanGetMessageThread = message.CanGetMessageThread;
            _message.CanGetStatistics = message.CanGetStatistics;
            _message.CanBeRepliedInAnotherChat = message.CanBeRepliedInAnotherChat;
            _message.CanGetViewers = message.CanGetViewers;
            _message.CanGetReadDate = message.CanGetReadDate;
            _message.ChatId = message.ChatId;
            _message.ContainsUnreadMention = message.ContainsUnreadMention;
            //_message.Content = message.Content;
            //_message.Date = message.Date;
            _message.EditDate = message.EditDate;
            _message.ForwardInfo = message.ForwardInfo;
            _message.Id = message.Id;
            _message.IsChannelPost = message.IsChannelPost;
            _message.IsOutgoing = message.IsOutgoing;
            _message.IsPinned = message.IsPinned;
            _message.MessageThreadId = message.MessageThreadId;
            _message.MediaAlbumId = message.MediaAlbumId;
            _message.ReplyMarkup = message.ReplyMarkup;
            _message.ReplyTo = message.ReplyTo;
            _message.SenderId = message.SenderId;
            _message.SendingState = message.SendingState;
            _message.SelfDestructType = message.SelfDestructType;
            _message.SelfDestructIn = message.SelfDestructIn;
            _message.AutoDeleteIn = message.AutoDeleteIn;
            _message.ViaBotUserId = message.ViaBotUserId;
            _message.InteractionInfo = message.InteractionInfo;
            _message.UnreadReactions = message.UnreadReactions;
            _message.RestrictionReason = message.RestrictionReason;
            _message.SavedMessagesTopicId = message.SavedMessagesTopicId;
            _message.ImportInfo = message.ImportInfo;
            _message.IsTopicMessage = message.IsTopicMessage;
            _message.HasTimestampedMedia = message.HasTimestampedMedia;
            _message.CanReportReactions = message.CanReportReactions;
            _message.CanGetMediaTimestampLinks = message.CanGetMediaTimestampLinks;
            _message.CanGetAddedReactions = message.CanGetAddedReactions;
            _message.SchedulingState = message.SchedulingState;

            _isSaved = null;

            if (_message.Content is MessageAlbum album)
            {
                FormattedText caption = null;
                StyledText text = null;

                if (album.IsMedia)
                {
                    foreach (var child in album.Messages)
                    {
                        var childCaption = child.GetCaption();
                        if (childCaption != null && !string.IsNullOrEmpty(childCaption.Text))
                        {
                            if (caption == null || string.IsNullOrEmpty(caption.Text))
                            {
                                caption = childCaption;
                                text = child.Text;
                            }
                            else
                            {
                                caption = null;
                                text = null;
                                break;
                            }
                        }
                    }
                }
                else if (album.Messages.Count > 0)
                {
                    caption = album.Messages[^1].GetCaption();
                    text = album.Messages[^1].Text;
                }

                album.Caption = caption ?? new FormattedText();
                Text = text;
            }
        }
    }

    public class MessageWithOwner
    {
        protected readonly IClientService _clientService;
        protected Message _message;

        public MessageWithOwner(IClientService clientService, Message message)
        {
            _clientService = clientService;
            _message = message;
        }

        public override string ToString()
        {
            return _message.ToString();
        }

        public MessageId CombinedId => new(this);

        public IClientService ClientService => _clientService;

        public ReplyMarkup ReplyMarkup { get => _message.ReplyMarkup; set => _message.ReplyMarkup = value; }
        public MessageContent Content { get => _message.Content; set => SetContent(value); }
        public long MediaAlbumId => _message.MediaAlbumId;
        public MessageInteractionInfo InteractionInfo { get => _message.InteractionInfo; set => _message.InteractionInfo = value; }
        public string AuthorSignature => _message.AuthorSignature;
        public long ViaBotUserId => _message.ViaBotUserId;
        public double SelfDestructIn { get => _message.SelfDestructIn; set => _message.SelfDestructIn = value; }
        public MessageSelfDestructType SelfDestructType => _message.SelfDestructType;
        public MessageReplyTo ReplyTo { get => _message.ReplyTo; set => _message.ReplyTo = value; }
        public MessageForwardInfo ForwardInfo => _message.ForwardInfo;
        public MessageImportInfo ImportInfo => _message.ImportInfo;
        public IList<UnreadReaction> UnreadReactions { get => _message.UnreadReactions; set => _message.UnreadReactions = value; }
        public int EditDate { get => _message.EditDate; set => _message.EditDate = value; }
        public int Date => _message.Date;
        public bool ContainsUnreadMention { get => _message.ContainsUnreadMention; set => _message.ContainsUnreadMention = value; }
        public bool IsChannelPost => _message.IsChannelPost;
        public bool IsTopicMessage => _message.IsTopicMessage;
        public bool CanBeDeletedForAllUsers => _message.CanBeDeletedForAllUsers;
        public bool CanBeDeletedOnlyForSelf => _message.CanBeDeletedOnlyForSelf;
        public bool CanBeRepliedInAnotherChat => _message.CanBeRepliedInAnotherChat;
        public bool CanBeForwarded => _message.CanBeForwarded;
        public bool CanBeEdited => _message.CanBeEdited;
        public bool CanBeSaved => _message.CanBeSaved;
        public bool CanGetMessageThread => _message.CanGetMessageThread;
        public bool CanGetStatistics => _message.CanGetStatistics;
        public bool CanGetViewers => _message.CanGetViewers;
        public bool CanGetReadDate => _message.CanGetReadDate;
        public bool IsOutgoing { get => _message.IsOutgoing; set => _message.IsOutgoing = value; }
        public bool IsPinned { get => _message.IsPinned; set => _message.IsPinned = value; }
        public bool HasTimestampedMedia => _message.HasTimestampedMedia;
        public MessageSchedulingState SchedulingState => _message.SchedulingState;
        public MessageSendingState SendingState => _message.SendingState;
        public long ChatId => _message.ChatId;
        public long MessageThreadId => _message.MessageThreadId;
        public MessageSender SenderId => _message.SenderId;
        public long Id => _message.Id;

        private void SetContent(MessageContent content)
        {
            _message.Content = content;
            SetText(content?.GetCaption());
        }

        protected void SetText(FormattedText caption)
        {
            if (caption != null && caption.Text.Length > 0)
            {
                Text = TextStyleRun.GetText(caption);
            }
            else
            {
                Text = null;
            }
        }

        public StyledText Text { get; set; }

        public MessageTranslateResult TranslatedText { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is Message y)
            {
                return Id == y.Id && ChatId == y.ChatId;
            }
            else if (obj is MessageWithOwner ym)
            {
                return Id == ym.Id && ChatId == ym.ChatId;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChatId, Id);
        }

        public Message Get()
        {
            return _message;
        }

        public virtual bool CanBeAddedToDownloads
        {
            get
            {
                if (ClientService.TryGetChat(ChatId, out var chat))
                {
                    return CanBeSaved && !chat.HasProtectedContent && Content is MessageAudio or MessageDocument or MessageVideo;
                }

                return false;
            }
        }
    }

    public enum MessageReplyToState
    {
        None,
        Loading,
        Deleted,
        Hidden
    }
}
