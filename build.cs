var root = Directory.GetCurrentDirectory();
foreach (var scripts in GetScripts(root))
{
    foreach (var script in scripts)
    {
        Console.WriteLine($"Compile script {script} test");
        CommandExecutor.ExecuteCommandAndOutput($"dotnet-exec {script} --dry-run").EnsureSuccessExitCode();
    }
}

IEnumerable<string[]> GetScripts(string root)
{
	foreach (var dir in Directory.GetDirectories(root))
	{
		yield return Directory.GetFiles(dir, "*.cs");
		
		foreach (var scripts in GetScripts(dir))
		{
			yield return scripts;
		}
	}
}