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
using System.Security.Authentication;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BitNaughts
{
    public static class Mainframe
    {
        [FunctionName("Ping")]
        public static async Task<IActionResult> Ping(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "ping")] HttpRequest req,
            ILogger log) {
            string telemetry = "Ping()\n";
            try {
                /* Get HTTP headers */
                string name = req?.Query["name"], data = req?.Query["data"];
                if (name == null || data == null) return new OkObjectResult("⚠ Exception: Name/Data missing;");

                /* Mongo Connection and TLS */
                MongoClientSettings settings = MongoClientSettings.FromUrl(new MongoUrl(System.Environment.GetEnvironmentVariable("MONGO_CONNECTIONSTRING")));
                settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
                var client = new MongoClient(settings);
                
                /* Mongo Database and Collection */
                var database = client.GetDatabase("MultiplayerState");
                var collection = database.GetCollection<BsonDocument>("MultiplayerStateTest");
                
                /* Mongo Update or Insert */
                var filter = Builders<BsonDocument>.Filter.Eq("name", name);
                var update = Builders<BsonDocument>.Update.Set("data", data);
                var options = new UpdateOptions(); options.IsUpsert = true;
                collection.UpdateOne(filter, update, options);
                
                /* Mongo Output */
                telemetry += "Mongo Output";
                foreach (var structure in collection.Find(new BsonDocument()).ToList())
                {
                    if(structure["name"] != name) telemetry += structure + "♖"; 
                }
                return new OkObjectResult(telemetry);
            } catch (Exception e) { 
                return new OkObjectResult(telemetry + $"⚠ Exception: {e.ToString()}");
            }
        }

        [FunctionName("Mainframe")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log) {
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



public CloneOptions cloningSSHAuthentication(string username, string path_to_public_key_file, string path_to_private_key_file)
    {
        CloneOptions options = new CloneOptions();
        SshUserKeyCredentials credentials = new SshUserKeyCredentials();
        credentials.Username = username;
        credentials.PublicKey = path_to_public_key_file;
        credentials.PrivateKey =  path_to_private_key_file;
        credentials.Passphrase = "ssh_key_password";
        options.CredentialsProvider = new LibGit2Sharp.Handlers.CredentialsHandler((url, usernameFromUrl, types) =>  credentials) ;
        return options;
    }

public CloneOptions cloneSSHAgent(string username){
        CloneOptions options = new CloneOptions();
        SshAgentCredentials credentials = new SshAgentCredentials();
        credentials.Username = username;
        var handl688er = new LibGit2Sharp.Handlers.CredentialsHandler((url, usernameFromUrl, types) => credentials);
        options.CredentialsProvider = handler;
        return options;

}

public void CloneRepo(string remotePath, string localPath){
    CloneOptions options = cloningSSHAuthentication("git", "C:\\folder\\id_rsa.pub", "C:\\folder\\id_rsa");
    Repository.Clone(remotePath, localPath, options);
}
