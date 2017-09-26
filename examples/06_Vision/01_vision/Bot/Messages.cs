using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using Newtonsoft.Json;

// Bot Storage: Register the optional private state storage for your bot. 

// For Azure Table Storage, set the following environment variables in your bot app:
// -UseTableStorageForConversationState set to 'true'
// -AzureWebJobsStorage set to your table connection string

// For CosmosDb, set the following environment variables in your bot app:
// -UseCosmosDbForConversationState set to 'true'
// -CosmosDbEndpoint set to your cosmos db endpoint
// -CosmosDbKey set to your cosmos db key

namespace Bot
{
  public static class Messages
  {
    [FunctionName("messages")]
    public static async Task<HttpResponseMessage> Run(
      [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req, TraceWriter log)
    {
      log.Info($"Webhook was triggered!");

      // Initialize the azure bot
      using (new AzureFunctionsResolveAssembly())
      using (BotService.Initialize())
      {
        // Deserialize the incoming activity
        string jsonContent = await req.Content.ReadAsStringAsync();
        var activity = JsonConvert.DeserializeObject<Activity>(jsonContent);

        // authenticate incoming request and add activity.ServiceUrl to MicrosoftAppCredentials.TrustedHostNames
        // if request is authenticated
        if (!await BotService.Authenticator.TryAuthenticateAsync(req, new[] {activity}, CancellationToken.None))
        {
          return BotAuthenticator.GenerateUnauthorizedResponse(req);
        }

        if (activity != null)
        {
          // one of these will have an interface and process it
          switch (activity.GetActivityType())
          {
            case ActivityTypes.Message:
              HandleImageUpload(activity);
              await Conversation.SendAsync(activity, () => new EchoLuisDialog());
              break;
            case ActivityTypes.ConversationUpdate:
              var client = new ConnectorClient(new Uri(activity.ServiceUrl));
              IConversationUpdateActivity update = activity;
              if (update.MembersAdded.Any())
              {
                var reply = activity.CreateReply();
                var newMembers = update.MembersAdded?.Where(t => t.Id != activity.Recipient.Id);
                foreach (var newMember in newMembers)
                {
                  reply.Text = "Welcome";
                  if (!string.IsNullOrEmpty(newMember.Name))
                  {
                    reply.Text += $" {newMember.Name}";
                  }
                  reply.Text += "!";
                  await client.Conversations.ReplyToActivityAsync(reply);
                }
              }
              break;
            case ActivityTypes.ContactRelationUpdate:
            case ActivityTypes.Typing:
            case ActivityTypes.DeleteUserData:
            case ActivityTypes.Ping:
            default:
              log.Error($"Unknown activity type ignored: {activity.GetActivityType()}");
              break;
          }
        }
        return req.CreateResponse(HttpStatusCode.Accepted);
      }
    }

    private static void HandleImageUpload(Activity activity)
    {
      var pattern = @"^image/(jpg|jpeg|png|gif)$";
      var regex = new Regex(pattern, RegexOptions.IgnoreCase);

      var attachment = ((List<Attachment>)activity.Attachments)?
        .FirstOrDefault(item => regex.Matches(item.ContentType).Count > 0);

      if (attachment != null && regex.IsMatch(attachment.ContentType))
      {
        activity.Text = attachment.ContentUrl;
      }
    }
  }
}
