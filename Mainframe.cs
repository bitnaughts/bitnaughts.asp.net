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
using StackExchange.Redis;
using System.Reflection;


namespace BitNaughts
{
    // public class FunctionsAssemblyResolver
    // {
    //     public static void RedirectAssembly()
    //     {
    //         var list = AppDomain.CurrentDomain.GetAssemblies().OrderByDescending(a => a.FullName).Select(a => a.FullName).ToList();
    //         AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
    //     }

    //     private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    //     {
    //         var requestedAssembly = new AssemblyName(args.Name);
    //         Assembly assembly = null;
    //         AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
    //         try
    //         {
    //             assembly = Assembly.Load(requestedAssembly.Name);
    //         }
    //         catch (Exception ex)
    //         {
    //         }
    //         AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
    //         return assembly;
    //     }

    // }
    public static class Mainframe
    {
        /* Redis Connection Multiplexer */
        private static Lazy<ConnectionMultiplexer> lazyConnection = CreateConnection();
        public static ConnectionMultiplexer Connection
        {
            get
            {
                return lazyConnection.Value;
            }
        }
        private static Lazy<ConnectionMultiplexer> CreateConnection()
        {
            return new Lazy<ConnectionMultiplexer>(() =>
            {
            var CONNECTION_STRING = System.Environment.GetEnvironmentVariable("REDIS_CONNECTIONSTRING");
            return ConnectionMultiplexer.Connect(CONNECTION_STRING);
            });
        }

        [FunctionName("Ping")]
        public static async Task<IActionResult> Ping(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "ping")] HttpRequest req,
            ILogger log)
        {
            // FunctionsAssemblyResolver.RedirectAssembly();
            string telemetry = "Ping()\n";
            try {
                // /* Get HTTP headers */
                string name = req?.Query["name"], data = req?.Query["data"], cursor = req?.Query["cursor"];
                var db = Connection.GetDatabase();   
                
                /* Set player's position*/
                telemetry += $" SET {name} {data}\n";
                await db.ExecuteAsync("SET", name, data);

                /* Scan for other players */
                telemetry += $" SCAN {cursor}\n";
                var scan_result = await db.ExecuteAsync("SCAN",  cursor);
                var scan_fi = scan_result.GetType().GetField("_value", BindingFlags.NonPublic|BindingFlags.Instance);
                var scan_res = (RedisResult[])scan_fi.GetValue(scan_result);
                var keys = (RedisResult[])scan_res[1];
                telemetry += $" NextCursor:{scan_res[0]}\n";

                foreach (RedisResult key in keys) {
                    var key_str = key.ToString();
                    telemetry += $" Key:{key_str}\n";
                    if (key_str != name) {

                        /* Get player data */
                        var player_result = await db.ExecuteAsync("GET", key_str);
                        var player_fi = player_result.GetType().GetField("_value", BindingFlags.NonPublic|BindingFlags.Instance);
                        var player_res = player_fi.GetValue(player_result);
                        telemetry += $" Value:{player_res}\n";
                    }
                }
                // var count = int.Parse(res[0].ToString());
                // var responseMessage = $"{{count:{count}}}";
                return new OkObjectResult(telemetry);//telemetry + $"\n{responseMessage}");
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
