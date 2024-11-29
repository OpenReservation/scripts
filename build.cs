var root = Directory.GetCurrentDirectory();
foreach (var scripts in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
{
    foreach (var script in scripts)
    {
        Console.WriteLine($"Compile script {script} test");
        CommandExecutor.ExecuteCommandAndOutput($"dotnet-exec {script} --dry-run").EnsureSuccessExitCode();
    }
}
