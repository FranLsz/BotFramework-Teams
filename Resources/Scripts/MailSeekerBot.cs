using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MailSeekerBot.Models;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;

namespace MailSeekerBot
{
    public class MailSeekerBot : IBot
    {
        private readonly LuisRecognizer _luisRecognizer;
        private readonly MailSeekerBotAccessors _stateAccessors;
        private readonly DialogSet _dialogs;

        public MailSeekerBot(MailSeekerBotAccessors accessors, LuisRecognizer luisRecognizer)
        {
            _stateAccessors = accessors ?? throw new ArgumentNullException(nameof(accessors));
            _luisRecognizer = luisRecognizer ?? throw new ArgumentNullException(nameof(luisRecognizer));

            _dialogs = new DialogSet(_stateAccessors.ConversationDialogState);
            _dialogs.Add(OAuthHelpers.Prompt(accessors.OAuthConnectionName));
            _dialogs.Add(new WaterfallDialog("graphDialog", new WaterfallStep[] { PromptStepAsync, ProcessStepAsync }));
        }

        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            switch (turnContext.Activity.Type)
            {
                case ActivityTypes.Message:
                    await ProcessMessageAsync(turnContext, cancellationToken);
                    break;
                case ActivityTypes.Event:
                case ActivityTypes.Invoke:

                    // nos aseguramos que estamos en Teams
                    if (turnContext.Activity.Type == ActivityTypes.Invoke && turnContext.Activity.ChannelId != "msteams")
                        throw new InvalidOperationException("El Invoke solo es valido desde Teams");

                    var dialogContext = await _dialogs.CreateContextAsync(turnContext, cancellationToken);
                    await dialogContext.ContinueDialogAsync(cancellationToken);
                    if (!turnContext.Responded)
                        await dialogContext.BeginDialogAsync("graphDialog", cancellationToken: cancellationToken);
                    break;
                case ActivityTypes.ConversationUpdate:
                    // saludamos cuando un usuario entra
                    if (turnContext.Activity.MembersAdded != null && turnContext.Activity.MembersAdded.Any(o => o.Id != turnContext.Activity.Recipient.Id))
                        await turnContext.SendActivityAsync($"Hola {turnContext.Activity.MembersAdded.First(o => o.Id != turnContext.Activity.Recipient.Id).Name}, bienvenido! Soy MailSeekerBot, puedes pedirme que busque por ti determinados emails", cancellationToken: cancellationToken);
                    break;
            }

            // persistimos los datos establecidos en este turno
            await _stateAccessors.ConversationState.SaveChangesAsync(turnContext, cancellationToken: cancellationToken);
            await _stateAccessors.UserState.SaveChangesAsync(turnContext, cancellationToken: cancellationToken);
        }

        private async Task<DialogContext> ProcessMessageAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var dialogContext = await _dialogs.CreateContextAsync(turnContext, cancellationToken);

            switch (turnContext.Activity.Text.ToLowerInvariant())
            {
                case "logout":
                    var botAdapter = (BotFrameworkAdapter)turnContext.Adapter;
                    await botAdapter.SignOutUserAsync(turnContext, _stateAccessors.OAuthConnectionName, cancellationToken: cancellationToken);

                    await turnContext.SendActivityAsync("Hecho, sesión cerrada", cancellationToken: cancellationToken);
                    break;
                case "help":
                    await turnContext.SendActivityAsync("Hola, puedo buscar por ti determinados emails, simplemente pregúntame!", cancellationToken: cancellationToken);
                    break;
                default:
                    await dialogContext.ContinueDialogAsync(cancellationToken);
                    if (!turnContext.Responded)
                        await dialogContext.BeginDialogAsync("graphDialog", cancellationToken: cancellationToken);
                    break;
            }

            return dialogContext;
        }

        private async Task<DialogTurnResult> PromptStepAsync(WaterfallStepContext step, CancellationToken cancellationToken)
        {
            var activity = step.Context.Activity;

            if (activity.Type == ActivityTypes.Message && !Regex.IsMatch(activity.Text, @"(\d{6})"))
                await _stateAccessors.CommandState.SetAsync(step.Context, activity.Text, cancellationToken);

            return await step.BeginDialogAsync("loginPrompt", cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> ProcessStepAsync(WaterfallStepContext step, CancellationToken cancellationToken)
        {
            var tokenResponse = step.Result as TokenResponse;

            // si no tenemos el token, significa que la autenticación ha fallado, por tanto no podremos llamar a las la API de Graph
            if (tokenResponse?.Token == null)
            {
                await step.Context.SendActivityAsync("El login no ha podido efectuarse correctamente, inténtalo más tarde", cancellationToken: cancellationToken);
                return await step.EndDialogAsync(cancellationToken: cancellationToken);
            }

            // si el texto viene vacio, significa que acabamos de logear, por tanto lo recuperamos del state
            // y cambiamos la activity a 'message' para que pueda procesarse por LUIS
            if (string.IsNullOrEmpty(step.Context.Activity.Text))
            {
                await step.Context.SendActivityAsync("Genial, has iniciado sesión!, cuando quieras cerrarla basta con decirme 'logout'", cancellationToken: cancellationToken);

                step.Context.Activity.Text = _stateAccessors.CommandState.GetAsync(step.Context, () => string.Empty, cancellationToken).Result;
                step.Context.Activity.Type = "message";

                await _stateAccessors.CommandState.DeleteAsync(step.Context, cancellationToken);
            }

            // procesamos el texto con LUIS para obtener la intencion
            var recognizerResult = await _luisRecognizer.RecognizeAsync(step.Context, cancellationToken);
            var topIntent = recognizerResult?.GetTopScoringIntent();
            var intent = (topIntent != null) ? topIntent.Value.intent : string.Empty;
            var score = topIntent?.score ?? 0;

            if (!string.IsNullOrEmpty(intent) && intent != "None" && score > 0.95)
            {
                switch (intent)
                {
                    case "General_Hello":
                        await step.Context.SendActivityAsync("Hola!", cancellationToken: cancellationToken);

                        break;
                    case "Mail_Get":
                        if (recognizerResult != null)
                        {
                            var mailFilter = new MailFilter()
                            {
                                From = recognizerResult.Entities.GetValue("Mail_From")?.Last.Value<string>() ?? string.Empty,
                                Subject = recognizerResult.Entities.GetValue("Mail_Subject")?.Last.Value<string>() ?? string.Empty,
                                Count = recognizerResult.Entities.GetValue("Mail_Count")?.Last.Value<int>() ?? 1 // si no especifica la cantidad, devolvemos el ultimo
                            };

                            await OAuthHelpers.ListMailAsync(step.Context, tokenResponse, mailFilter);
                        };

                        break;
                    default:
                        await step.Context.SendActivityAsync($"Intent: {intent} ({score}).", cancellationToken: cancellationToken);
                        break;
                }
            }
            else
                await step.Context.SendActivityAsync("No te entiendo...", cancellationToken: cancellationToken);

            return await step.EndDialogAsync(cancellationToken: cancellationToken);
        }
    }
}
