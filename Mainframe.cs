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

namespace BitNaughts
{
    public static class Mainframe
    {
        [FunctionName("Mainframe")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string name = req?.Query["name"], 
                   data = req?.Query["data"], 
                   db_folder = Path.GetTempPath() + "db",
                   logMessage = "";

            if (name == null) 
                return new BadRequestObjectResult("Missing name!");

            string directory = db_folder + "/" + name + ".txt";
            if (!Directory.Exists(db_folder))  
            {  
                log.LogInformation("C# HTTP function: Repository.Clone");
                Repository.Clone("https://github.com/bitnaughts/bitnaughts.db.git", db_folder);
            }
            using (var repo = new Repository(db_folder))
            {
                log.LogInformation("C# HTTP function: Repository.Fetch");
                var remote = repo.Network.Remotes["origin"];
                var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                Commands.Fetch(repo, remote.Name, refSpecs, null, logMessage);

                if (System.IO.File.Exists(directory)) 
                    if (data == null) 
                        return new OkObjectResult(System.IO.File.ReadAllText(directory));
                    else 
                        return new BadRequestObjectResult(name + " already exists!");
                else if (data == null) 
                    return new BadRequestObjectResult(name + " doesn't exist!");
                
                UnicodeEncoding unicode = new UnicodeEncoding();
                using (System.IO.FileStream fs = System.IO.File.Create(directory))
                    fs.Write(unicode.GetBytes(data), 0, unicode.GetByteCount(data));

                log.LogInformation("C# HTTP function: Repository.Add");
                repo.Index.Add(name + ".txt");
                repo.Index.Write();

                log.LogInformation("C# HTTP function: Repository.Commit");
                Signature author = new Signature("Mutilar", "brianhungerman@gmail.com", DateTime.Now);
                Signature committer = new Signature(System.Environment.GetEnvironmentVariable("GH_USERNAME"), "botnaughts@gmail.com", DateTime.Now);
                Commit commit = repo.Commit(name, author, committer);

                log.LogInformation("C# HTTP function: Repository.Push");
                var options = new PushOptions();
                options.CredentialsProvider = (_url, _user, _cred) => 
                new UsernamePasswordCredentials { 
                    Username = System.Environment.GetEnvironmentVariable("GH_USERNAME"), 
                    Password = System.Environment.GetEnvironmentVariable("GH_PASSWORD") };
                repo.Network.Push(remote, @"refs/heads/master", options);
                
                return new OkObjectResult(name + " saved!");
            }
        }
    }
}
