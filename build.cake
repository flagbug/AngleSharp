/* ****************************************
   Publishing workflow
   -------------------

 - Update CHANGELOG.md
 - Run a normal build with Cake
 - Push to devel and FF merge to master
 - Switch to master
 - Run a Publish build with Cake
 - Switch back to devel branch
   **************************************** */

#addin "Cake.FileHelpers"
#addin "Octokit"
using Octokit;

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var isLocal = BuildSystem.IsLocalBuild;
var isRunningOnUnix = IsRunningOnUnix();
var isRunningOnWindows = IsRunningOnWindows();
var isRunningOnAppVeyor = AppVeyor.IsRunningOnAppVeyor;
var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
var buildNumber = AppVeyor.Environment.Build.Number;
var releaseNotes = ParseReleaseNotes("./CHANGELOG.md");
var version = releaseNotes.Version.ToString();
var variants = new String[] { "AngleSharp", "AngleSharp.Legacy", "AngleSharp.Silverlight" };
var buildDirs = variants.Select(variant => Directory("./src/" + variant + "/bin") + Directory(configuration)).ToArray();
var buildResultDir = Directory("./bin") + Directory(version);
var nugetRoot = buildResultDir + Directory("nuget");

// Initialization
// ----------------------------------------

Setup(() =>
{
    Information("Building version {0} of AngleSharp.", version);
    Information("For the publish target the following environment variables need to be set:");
    Information("  NUGET_API_KEY, GITHUB_API_TOKEN");
});

// Tasks
// ----------------------------------------

Task("Clean")
    .Does(() =>
    {
        CleanDirectories(buildDirs.OfType<DirectoryPath>().Concat(new DirectoryPath[] { buildResultDir, nugetRoot }));
    });

Task("Restore-Packages")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        NuGetRestore("./src/AngleSharp.Core.sln");
    });

Task("Build")
    .IsDependentOn("Restore-Packages")
    .Does(() =>
    {
        if (isRunningOnWindows)
        {
            MSBuild("./src/AngleSharp.Core.sln", new MSBuildSettings()
                .SetConfiguration(configuration)
                .UseToolVersion(MSBuildToolVersion.NET40)
                .SetPlatformTarget(PlatformTarget.MSIL)
                .SetMSBuildPlatform(MSBuildPlatform.x86)
                .SetVerbosity(Verbosity.Minimal)
            );
        }
        else
        {
            XBuild("./src/AngleSharp.Core.sln", new XBuildSettings()
                .SetConfiguration(configuration)
                .SetVerbosity(Verbosity.Minimal)
            );
        }
    });

Task("Run-Unit-Tests")
    .IsDependentOn("Build")
    .Does(() =>
    {
        var settings = new NUnit3Settings
        {
            Work = buildResultDir.Path.FullPath
        };

        if (isRunningOnAppVeyor)
        {
            settings.Where = "cat != ExcludeFromAppVeyor";
        }

        NUnit3("./src/**/bin/" + configuration + "/*.Tests.dll", settings);
    });

Task("Copy-Files")
    .IsDependentOn("Build")
    .Does(() =>
    {
        var mapping = new Dictionary<String, Int32>
        {
            { "net45", 0 },
            { "portable-windows8+net45+windowsphone8+wpa", 0 },
            { "dotnet", 0 },
            { "net40", 1 },
            { "sl50", 2 },
        };

        foreach (var item in mapping)
        {
            var target = nugetRoot + Directory("lib") + Directory(item.Key);
            CreateDirectory(target);
            CopyFiles(new FilePath[]
            { 
                buildDirs[item.Value] + File("AngleSharp.dll"),
                buildDirs[item.Value] + File("AngleSharp.xml")
            }, target);
        }

        CopyFiles(new FilePath[] { "src/AngleSharp.nuspec" }, nugetRoot);
    });

Task("Create-Package")
    .IsDependentOn("Copy-Files")
    .Does(() =>
    {
        var nugetExe = GetFiles("./tools/**/nuget.exe").FirstOrDefault()
            ?? (isRunningOnAppVeyor ? GetFiles("C:\\Tools\\NuGet3\\nuget.exe").FirstOrDefault() : null);

        if (nugetExe == null)
        {            
            throw new InvalidOperationException("Could not find nuget.exe.");
        }
        
        var nuspec = nugetRoot + File("AngleSharp.nuspec");
        
        NuGetPack(nuspec, new NuGetPackSettings
        {
            Version = version,
            OutputDirectory = nugetRoot,
            Symbols = false,
            Properties = new Dictionary<String, String> { { "Configuration", configuration } }
        });
    });
    
Task("Publish-Package")
    .IsDependentOn("Create-Package")
    .WithCriteria(() => isLocal)
    .Does(() =>
    {
        var apiKey = EnvironmentVariable("NUGET_API_KEY");

        if (String.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Could not resolve the NuGet API key.");
        }

        foreach (var nupkg in GetFiles(nugetRoot.Path.FullPath + "/*.nupkg"))
        {
            NuGetPush(nupkg, new NuGetPushSettings { ApiKey = apiKey });
        }
    });
    
Task("Publish-Release")
    .IsDependentOn("Publish-Package")
    .WithCriteria(() => isLocal)
    .Does(() =>
    {
        var githubToken = EnvironmentVariable("GITHUB_API_TOKEN");

        if (String.IsNullOrEmpty(githubToken))
        {
            throw new InvalidOperationException("Could not resolve AngleSharp GitHub token.");
        }
        
        var github = new GitHubClient(new ProductHeaderValue("AngleSharpCakeBuild"))
        {
            Credentials = new Credentials(githubToken)
        };

        github.Release.Create("AngleSharp", "AngleSharp", new NewRelease("v" + version) 
        {
            Name = version,
            Body = String.Join(Environment.NewLine, releaseNotes.Notes),
            Prerelease = false,
            TargetCommitish = "master"
        }).Wait();
    });
    
Task("Update-AppVeyor-Build-Number")
    .WithCriteria(() => isRunningOnAppVeyor)
    .Does(() =>
    {
        AppVeyor.UpdateBuildVersion(version);
    });
    
// Targets
// ----------------------------------------
    
Task("Package")
    .IsDependentOn("Run-Unit-Tests")
    .IsDependentOn("Create-Package");

Task("Default")
    .IsDependentOn("Package");    

Task("Publish")
    .IsDependentOn("Publish-Package")
    .IsDependentOn("Publish-Release");
    
Task("AppVeyor")
    .IsDependentOn("Run-Unit-Tests")
    .IsDependentOn("Update-AppVeyor-Build-Number");

// Execution
// ----------------------------------------

RunTarget(target);