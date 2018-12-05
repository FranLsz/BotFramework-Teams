using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using MailSeekerBot.Models;
using Microsoft.Graph;

namespace MailSeekerBot
{
    public class GraphClient
    {
        private readonly string _token;

        public GraphClient(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentNullException(nameof(token));

            _token = token;
        }

        // Obtenemos el listado de mails a partir de los filtros
        public async Task<Message[]> GetMailAsync(MailFilter mailFilter)
        {
            var graphClient = GetAuthenticatedClient();
            var messages = await graphClient.Me.MailFolders.Inbox.Messages.Request().Top(100).GetAsync();

            return messages
                .Where(o => (string.IsNullOrEmpty(mailFilter.From) || o.From.EmailAddress.Name.ToLower().Contains(mailFilter.From))
                && (string.IsNullOrEmpty(mailFilter.Subject) || o.Subject.ToLower().Contains(mailFilter.Subject)))
                .Take(mailFilter.Count)
                .ToArray();
        }

        // Obtenemos el cliente de MS Graph a partir del token
        private GraphServiceClient GetAuthenticatedClient()
        {
            var graphClient = new GraphServiceClient(
                new DelegateAuthenticationProvider(
                    requestMessage =>
                    {
                        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", _token);
                        requestMessage.Headers.Add("Prefer", "outlook.timezone=\"" + TimeZoneInfo.Local.Id + "\"");

                        return Task.CompletedTask;
                    }));
            return graphClient;
        }
    }
}
