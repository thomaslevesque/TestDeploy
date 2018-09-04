namespace FakeItEasy.Deploy
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using Octokit;
    using static SimpleExec.Command;

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var releaseName = Environment.GetEnvironmentVariable("APPVEYOR_REPO_TAG_NAME");
            if (string.IsNullOrEmpty(releaseName))
            {
                Console.WriteLine("No Appveyor tag name supplied. Not deploying.");
                return;
            }

            var (repoOwner, repoName) = GetRepositoryName();
            var gitHubClient = GetAuthenticatedGitHubClient();

            Console.WriteLine($"Deploying {releaseName})");
            Console.WriteLine($"Looking for GitHub release ${releaseName}");

            var release = await gitHubClient.Repository.Release.Get(repoOwner, repoName, releaseName)
                ?? throw new Exception($"Can't find release {releaseName}");

            var artifactsFolder = "artifacts/output/";
            var artifactsPattern = "*.nupkg";

            var artifacts = Directory.GetFiles(artifactsFolder, artifactsPattern);
            if (!artifacts.Any())
            {
                throw new Exception("Can't find any artifacts to publish");
            }

            Console.WriteLine($"Uploading artifacts to GitHub release {releaseName}");
            var uploadClient = GetAuthenticatedGitHubUploadClient();
            foreach (var file in artifacts)
            {
                await UploadArtifactToGitHubReleaseAsync(uploadClient, release, file);
            }

            Console.WriteLine("Pushing nupkgs to nuget.org");
            foreach (var file in artifacts)
            {
                await UploadPackageToNuGetAsync(file);
            }

            Console.WriteLine("Finished deploying");
        }

        private static async Task UploadArtifactToGitHubReleaseAsync(HttpClient client, Release release, string path)
        {
            var name = Path.GetFileName(path);
            Console.WriteLine($"Uploading {name}");
            var uploadUrl = $"{release.UploadUrl}?name={Uri.EscapeDataString(name)}";
            using (var stream = File.OpenRead(path))
            {
                var content = new StreamContent(stream)
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue("application/octet-stream")
                    }
                };
                using (var response = await client.PostAsync(uploadUrl, content))
                {
                    response.EnsureSuccessStatusCode();
                    Console.WriteLine($"Uploading {name}");
                }
            }
        }

        private static async Task UploadPackageToNuGetAsync(string path)
        {
            const string nugetServer = "https://www.nuget.org/api/v2/package";
            string name = Path.GetFileName(path);
            Console.WriteLine($"Pushing {name}");
            await SimpleExec.Command.RunAsync(ToolPaths.NuGet, $"{path} -ApiKey {GetNuGetApiKey()} -Source {nugetServer} -NonInteractive -ForceEnglishOutput");
            Console.WriteLine($"Pushed {name}");
        }

        private static (string repoOwner, string repoName) GetRepositoryName()
        {
            var repoNameWithOwner = Environment.GetEnvironmentVariable("APPVEYOR_REPO_NAME");
            var parts = repoNameWithOwner.Split('/');
            return (parts[0], parts[1]);
        }

        private static GitHubClient GetAuthenticatedGitHubClient()
        {
            var token = GetGitHubToken();
            var credentials = new Credentials(token);
            return new GitHubClient(new Octokit.ProductHeaderValue("FakeItEasy-build-scripts")) { Credentials = credentials };
        }

        private static HttpClient GetAuthenticatedGitHubUploadClient()
        {
            return new HttpClient
            {
                DefaultRequestHeaders =
                {
                    Authorization = new AuthenticationHeaderValue("Bearer", GetGitHubToken())
                }
            };
        }

        private static string GetGitHubToken() => Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        private static string GetNuGetApiKey() => Environment.GetEnvironmentVariable("NUGET_API_KEY");

        private static string GetCurrentScriptDirectory([CallerFilePath] string path = null) => Path.GetDirectoryName(path);
    }
}
