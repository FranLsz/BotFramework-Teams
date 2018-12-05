using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MailSeekerBot.Models;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;

namespace MailSeekerBot
{
    public static class OAuthHelpers
    {
        // Obtiene los mails del usuario en base a unos filtros
        public static async Task ListMailAsync(ITurnContext turnContext, TokenResponse tokenResponse, MailFilter mailFilter)
        {
            if (turnContext == null)
                throw new ArgumentNullException(nameof(turnContext));

            if (tokenResponse == null)
                throw new ArgumentNullException(nameof(tokenResponse));

            await turnContext.SendActivityAsync("Dame un momento, estoy buscando...");
            // solo para depurar y validar el entrenamiento de LUIS
            await turnContext.SendActivityAsync($"From ({mailFilter.From}) - Subject ({mailFilter.Subject}) - Count ({mailFilter.Count})");


            var client = new GraphClient(tokenResponse.Token);
            var messages = await client.GetMailAsync(mailFilter);
            var reply = turnContext.Activity.CreateReply();


            if (messages.Any())
            {
                await turnContext.SendActivityAsync($"Terminé!, he encontrado {messages.Length} mail");

                reply.Attachments = new List<Attachment>();
                reply.AttachmentLayout = AttachmentLayoutTypes.Carousel;

                foreach (var mail in messages)
                {
                    var card = new HeroCard(
                        mail.Subject,
                        $"{mail.From.EmailAddress.Name} <{mail.From.EmailAddress.Address}>",
                        mail.BodyPreview,
                        new List<CardImage>()
                        {
                            new CardImage("https://botframeworksamples.blob.core.windows.net/samples/OutlookLogo.jpg", "Outlook Logo"),
                        },
                        new List<CardAction>
                        {
                            new CardAction(ActionTypes.OpenUrl, "Ver mail", value: mail.WebLink)
                        }
                    );

                    reply.Attachments.Add(card.ToAttachment());
                }
            }
            else
                reply.Text = "No he encontrado nada...";

            await turnContext.SendActivityAsync(reply);
        }

        // Solicitamos al usuario que inicie sesión en caso de no haberlo hecho ya
        public static OAuthPrompt Prompt(string connectionName)
        {
            return new OAuthPrompt(
                "loginPrompt",
                new OAuthPromptSettings
                {
                    ConnectionName = connectionName,
                    Text = "Por favor, inicia sesión",
                    Title = "Login",
                    Timeout = 300000, // tiene 5 min para iniciar sesión
                });
        }
    }
}
