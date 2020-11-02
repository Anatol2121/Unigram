﻿using System;
using System.Collections.Generic;
using Telegram.Td.Api;
using Unigram.Controls;

namespace Unigram.ViewModels.Delegates
{
    public interface IChatsDelegate : IViewModelDelegate
    {
        void ShowChatsUndo(IList<Chat> chats, UndoType type, Action<IList<Chat>> undo, Action<IList<Chat>> action = null);

        void SetSelectionMode(bool enabled);

        void SetSelectedItem(Chat chat);
        void SetSelectedItems(IList<Chat> chats);

        bool IsItemSelected(Chat chat);


        void Navigate(object item);

        void UpdateChatLastMessage(Chat chat);
    }

    public interface IChatListDelegate
    {
        long? SelectedItem { get; set; }
    }
}
