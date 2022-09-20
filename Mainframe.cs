using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using LibGit2Sharp;
using System.Reflection;


namespace BitNaughts
{
    // public class Functions
    public static class Mainframe
    {
        /*
            First Pass:  Use Cosmos No-SQL Mongo DB API (1 query to set KVs, 1 query to get all other KVs)
            Second Pass: Use Redis Key/Value Pairs (1 query to set key, 1 query for key set, N queries for values)
            Third Pass:  Use Redis Key/Null Value (1 query to set key, 1 query for key set (which has all the values))
        */
        [FunctionName("Ping")]
        public static async Task<IActionResult> Ping(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "ping")] HttpRequest req,
            ILogger log)
        {
            string telemetry = "Ping()\n";
            try {
                /* Get HTTP headers */
                string name = req?.Query["name"], data = req?.Query["data"], cursor = req?.Query["cursor"];

                /* Mongo Connection and TLS */
                MongoClientSettings settings = MongoClientSettings.FromUrl(new MongoUrl(System.Environment.GetEnvironmentVariable("MONGO_CONNECTIONSTRING")));
                settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
                var client = new MongoClient(settings);
                var database = client.GetDatabase("MultiplayerState");

                /* Set Player's Position*/
                telemetry += $" SET {name} {data}\n";
                database.Collections.Add(data);

                /* Return Other Players' Positions */
                telemetry += database.Collections.Find();
                return new OkObjectResult(telemetry);
            } catch (Exception e) { 
                return new OkObjectResult(telemetry + $"\n⚠ Exception {e.ToString()}");
            }
        }

        [FunctionName("Mainframe")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            string telemetry = "\n~ Mainframe";
            try {
                string name = req?.Query["name"], 
                    user_author = req?.Query["author"],
                    bot_author = "botnaughts@gmail.com",
                    data = req?.Query["data"], 
                    db_folder = Path.GetTempPath() + "db",
                    logMessage = "";

                if (name == null) return new BadRequestObjectResult(telemetry + "\n> Missing identifier!");
                if (user_author == null) user_author = bot_author;

                string directory = db_folder + "/" + name + ".txt";
                if (!Directory.Exists(db_folder))  
                {  
                    telemetry += "\n> <i>git clone bitnaughts.db.git</i>";
                    Repository.Clone("https://github.com/bitnaughts/bitnaughts.db.git", db_folder);
                }
                using (var repo = new Repository(db_folder))
                {
                    telemetry += "\n> <i>git fetch</i>";
                    var remote = repo.Network.Remotes["origin"];
                    var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                    Commands.Fetch(repo, remote.Name, refSpecs, null, logMessage);
                    if (System.IO.File.Exists(directory)) 
                        if (data == null) 
                            return new OkObjectResult(telemetry + "\n> " + System.IO.File.ReadAllText(directory, Encoding.Unicode));
                        else 
                            return new OkObjectResult(telemetry + "\n> Identifier exists!");
                    else if (data == null) 
                        return new OkObjectResult(telemetry + "\n> Identifier missing!");

                    // Credential information to fetch
                    telemetry += "\n> <i>git pull</i>";
                    PullOptions pullOptions = new PullOptions();
                    pullOptions.FetchOptions = new FetchOptions();
                    pullOptions.FetchOptions.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { 
                        Username = System.Environment.GetEnvironmentVariable("GH_USERNAME"), 
                        Password = System.Environment.GetEnvironmentVariable("GH_PASSWORD") };
                    // User information to create a merge commit
                    var signature = new Signature(new Identity(System.Environment.GetEnvironmentVariable("GH_USERNAME"), bot_author), DateTimeOffset.Now);

                    // Pull
                    Commands.Pull(repo, signature, pullOptions);
                                
                    UnicodeEncoding unicode = new UnicodeEncoding();
                    using (System.IO.FileStream fs = System.IO.File.Create(directory))
                        fs.Write(unicode.GetBytes(data), 0, unicode.GetByteCount(data));

                    telemetry += "\n> <i>git add " + name + ".txt</i>";
                    repo.Index.Add(name + ".txt");
                    repo.Index.Write();

                    telemetry += "\n> <i>git commit</i>";
                    Signature author = new Signature(System.Environment.GetEnvironmentVariable("GH_USERNAME"), user_author, DateTime.Now);
                    Signature committer = new Signature(System.Environment.GetEnvironmentVariable("GH_USERNAME"), bot_author, DateTime.Now);
                    Commit commit = repo.Commit(name, author, committer);

                    telemetry += "\n> <i>git push</i>";
                    var pushOptions = new PushOptions();
                    pushOptions.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { 
                        Username = System.Environment.GetEnvironmentVariable("GH_USERNAME"), 
                        Password = System.Environment.GetEnvironmentVariable("GH_PASSWORD") };
                    repo.Network.Push(remote, @"refs/heads/master", pushOptions);
                    
                    return new OkObjectResult(telemetry + "\n> Identifier saved!");
                }
            } catch (Exception e) {
                return new OkObjectResult(telemetry + "\n> Exception!" + "\n> " + e.ToString());
            }
        }
    }
}
