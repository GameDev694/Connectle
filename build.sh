#!/bin/bash
set -e  # Останавливаться при ошибках

echo "=== Installing .NET 8 ==="
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 8.0.100

echo "=== Setting up environment ==="
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH

echo "=== DotNet Info ==="
dotnet --info

echo "=== Restoring dependencies ==="
dotnet restore

echo "=== Building project ==="
dotnet build -c Release --no-restore

echo "=== Publishing project ==="
dotnet publish -c Release -o output --no-build

echo "=== Build successful! ==="
ls -la output/
