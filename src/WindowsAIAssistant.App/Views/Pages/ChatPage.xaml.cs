using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindowsAIAssistant.App.ViewModels;

namespace WindowsAIAssistant.App.Views.Pages;

public sealed partial class ChatPage : Page
{
    public ChatPage(ChatViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public ChatViewModel ViewModel { get; }

    /// <summary>
    /// Sends the message when Enter is pressed on its own.
    /// <para>
    /// The gesture is declared as a keyboard accelerator rather than handled in a key event so
    /// the platform does the modifier matching: a plain Enter and a Shift+Enter are registered
    /// separately, and the more specific one wins. Both end at the same command, so the
    /// composer and the send button cannot disagree about what is sendable. Shift+Enter is left
    /// unhandled on purpose, because a multi-line composer needs some way to ask about
    /// something that spans lines.
    /// </para>
    /// </summary>
    private void OnEnter(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Claiming the key keeps the composer from also inserting a blank line, which would
        // otherwise leave an empty line in the box after every send.
        args.Handled = true;

        if (ViewModel.SendCommand.CanExecute(null))
        {
            ViewModel.SendCommand.Execute(null);
        }
    }

    /// <summary>
    /// Leaves Shift+Enter to the text box so it inserts a newline.
    /// </summary>
    private void OnShiftEnter(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = false;
    }
}
