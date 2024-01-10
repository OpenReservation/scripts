dotnet tool update -g dotnet-execute
export PATH="$PATH:$HOME/.dotnet/tools"
#
dotnet-exec ./build.cs
