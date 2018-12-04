using System;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;

namespace MailSeekerBot
{
    public class MailSeekerBotAccessors
    {
        public MailSeekerBotAccessors(string oAuthConnectionName, ConversationState conversationState, UserState userState)
        {
            OAuthConnectionName = oAuthConnectionName ?? throw new ArgumentNullException(nameof(oAuthConnectionName));
            UserState = userState ?? throw new ArgumentNullException(nameof(userState));
            ConversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));
        }

        public static readonly string DialogStateName = $"{nameof(MailSeekerBotAccessors)}.DialogState";
        public static readonly string CommandStateName = $"{nameof(MailSeekerBotAccessors)}.CommandState";

        public IStatePropertyAccessor<DialogState> ConversationDialogState { get; set; }
        public IStatePropertyAccessor<string> CommandState { get; set; }

        public UserState UserState { get; }
        public ConversationState ConversationState { get; }
        public string OAuthConnectionName { get; }
    }
}
