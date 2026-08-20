foreach (var script in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.cs", SearchOption.AllDirectories))
{
    Console.WriteLine($"Compile script {script} test begin");
    CommandExecutor.ExecuteCommandAndOutput($"dotnet-exec {script} --generator --dry-run").EnsureSuccessExitCode();
    Console.WriteLine($"Compile script {script} test pass");
}
