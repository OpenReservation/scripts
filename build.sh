dotnet tool update -g dotnet-execute
export PATH="$PATH:$HOME/.dotnet/tools"
#
dotnet-exec info
dotnet-exec ./build.cs
