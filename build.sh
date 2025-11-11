#!/bin/bash

# Устанавливаем .NET 8
echo "Installing .NET 8..."
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0

export PATH="$HOME/.dotnet:$PATH"

echo "DotNet version:"
dotnet --version

echo "Restoring dependencies..."
dotnet restore

echo "Building project..."
dotnet publish -c Release -o output

echo "Build completed!"
