#!/bin/bash
# Run this script AFTER you install the .NET 8 SDK
if ! command -v dotnet &> /dev/null
then
    echo "❌ dotnet command could not be found. Please install the .NET 8 SDK first."
    exit 1
fi

echo "🚀 Initializing KnobForge Project..."
dotnet new sln -n KnobForge
dotnet new wpf -n KnobForge
dotnet sln KnobForge.sln add KnobForge/KnobForge.csproj

echo "📦 Installing Graphics Packages..."
cd KnobForge
dotnet add package SkiaSharp --version 2.88.6
dotnet add package SkiaSharp.Views.WPF --version 2.88.6

# Note: Check if specific native assets are needed for your Mac architecture
# dotnet add package SkiaSharp.NativeAssets.macOS 

echo "✅ Project Hydrated! Open KnobForge.sln in Visual Studio or VS Code."
