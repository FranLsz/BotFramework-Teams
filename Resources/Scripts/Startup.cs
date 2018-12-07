using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Integration;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Configuration;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailSeekerBot
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        private ILoggerFactory _loggerFactory;
        private readonly bool _isProduction;

        public Startup(IHostingEnvironment env)
        {
            _isProduction = env.IsProduction();
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddBot<MailSeekerBot>(options =>
            {
                var botFilePath = Configuration.GetSection("botFilePath")?.Value;
                var secretKey = Configuration.GetSection("botFileSecret")?.Value;

                // Cargamos el archivo de configuracion .bot y añadimos un singleton para que sea accesible por el Bot mediante IoC
                var botConfig = BotConfiguration.Load(botFilePath ?? @".\BotConfiguration.bot", secretKey);
                services.AddSingleton<BotConfiguration>(sp => botConfig ?? throw new InvalidOperationException($"The .bot config file could not be loaded. ({botConfig})"));

                // Recuperamos el entorno
                var environment = _isProduction ? "production" : "development";
                var service = botConfig.Services.FirstOrDefault(s => s.Type == "endpoint" && s.Name == environment);
                if (!(service is EndpointService endpointService))
                    throw new InvalidOperationException($"The .bot file does not contain an endpoint with name '{environment}'.");

                options.CredentialProvider = new SimpleCredentialProvider(endpointService.AppId, endpointService.AppPassword);

                // Creamos el logger para el registro de errores
                ILogger logger = _loggerFactory.CreateLogger<MailSeekerBot>();

                options.OnTurnError = async (context, exception) =>
                {
                    logger.LogError($"Exception caught : {exception}");
                    await context.SendActivityAsync("Vaya, parece que algo ha fallado...");
                };

                // Usaremos almacenamiento en memoria, esto quiere decir que cuando reiniciemos la web app se perderá el contexto (borrado de memoria)
                // Para Bots finales, lo ideal sería utilizar CosmosDB o Table Storage
                IStorage dataStore = new MemoryStorage();

                // Este objeto persistirá lo relacionado con la conversación
                var conversationState = new ConversationState(dataStore);
                options.State.Add(conversationState);

                // Este objeto persistirá lo relacionado con el usuario
                var userState = new UserState(dataStore);
                options.State.Add(userState);
            });

            // Configuramos y añadimos el singleton de LUIS para que sea accesible por el bot
            services.AddSingleton<LuisRecognizer>(sp =>
            {
                try
                {
                    // Credenciales de Luis
                    var luisApp = new LuisApplication(
                        applicationId: Configuration.GetSection("luisApplicationId")?.Value,
                        endpointKey: Configuration.GetSection("luisEndpointKey")?.Value,
                        endpoint: Configuration.GetSection("luisEndpoint")?.Value);

                    // Opciones
                    var luisPredictionOptions = new LuisPredictionOptions
                    {
                        IncludeAllIntents = true,
                    };

                    return new LuisRecognizer(luisApp, luisPredictionOptions, true);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    return null;
                }
            });

            // Configuramos y añadimos el singleton del Accessor de nuestro Bot
            services.AddSingleton<MailSeekerBotAccessors>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<BotFrameworkOptions>>().Value;
                if (options == null)
                    throw new InvalidOperationException("BotFrameworkOptions must be configured prior to setting up the State Accessors");

                var conversationState = options.State.OfType<ConversationState>().FirstOrDefault();
                if (conversationState == null)
                    throw new InvalidOperationException("ConversationState must be defined and added before adding conversation-scoped state accessors.");

                var userState = options.State.OfType<UserState>().FirstOrDefault();
                if (userState == null)
                    throw new InvalidOperationException("UserState must be defined and added before adding conversation-scoped state accessors.");

                var accessors = new MailSeekerBotAccessors(Configuration.GetSection("OauthConnectionName")?.Value, conversationState, userState)
                {
                    CommandState = userState.CreateProperty<string>(MailSeekerBotAccessors.CommandStateName),
                    ConversationDialogState = conversationState.CreateProperty<DialogState>(MailSeekerBotAccessors.DialogStateName),
                };

                return accessors;
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            _loggerFactory = loggerFactory;

            app.UseBotFramework();

            app.Run(async (context) =>
            {
                await context.Response.WriteAsync("MailSeekerBot iniciado!");
            });
        }
    }
}
