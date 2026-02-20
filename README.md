# KnobForge

A 3D tool for designing and texturing skeuomorphic UI components like knobs, dials, and buttons. KnobForge provides a real-time 3D environment to import models, define materials, set up complex lighting, and paint directly on the model to achieve detailed weathering effects.

![KnobForge UI](./.github/assets/knobforge-ui-screenshot.png)

## Core Features

*   **3D Model Import:** Import custom models in `.stl` and `.glb` formats to use as the base for your component.
*   **Advanced Lighting:** A flexible lighting system with multiple configurable point lights, environment lighting, and detailed shadow controls.
*   **Physically-Based Materials:** Adjust material properties like base color, metallic, roughness, and pearlescence to create realistic surfaces.
*   **Texture Painting:** Paint directly onto the 3D model using a channel-based system for effects like `Rust`, `Wear`, `Gunk`, and `Scratches`.
*   **Procedural Brushes:** A variety of brush types are available, including `Spray`, `Splat`, and `Scuff`, which use procedural generation for organic-looking strokes.
*   **Real-time 3D Viewport:** See the results of your work immediately in a live-rendered 3D viewport.

## Example Output

The tool can be used to generate spritesheets of rendered knobs for use in UIs.

![Knob Spritesheet](./.github/assets/knob-spritesheet.png)

## Getting Started

### Prerequisites

*   [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Building

Clone the repository and run the following command from the root directory to build the entire solution:

```bash
dotnet build KnobForge.sln
```

### Running the Application

To launch the KnobForge application, run the following command:

```bash
dotnet run --project KnobForge.App/KnobForge.App.csproj
```

#### Note for macOS Users

The rendering backend on macOS can be specified using an environment variable. For example, to use Metal (the default):

```bash
export KNOBFORGE_RENDER_MODE=metal
dotnet run --project KnobForge.App/KnobForge.App.csproj
```

Supported values are `metal`, `opengl`, and `software`.

## How to Use

1.  **Launch the application.** It will load a default project with an Ouroboros ring model.
2.  **Adjust Lighting:** Use the light controls in the UI to move lights, change their color, and adjust intensity.
3.  **Edit Materials:** Select the `CollarNode` or `MaterialNode` in the scene hierarchy to access and modify material properties in the inspector.
4.  **Paint Textures:**
    *   Enable the **Brush Painting** toggle.
    *   Select a **Paint Channel** (e.g., `Rust`, `Wear`).
    *   Choose a **Brush Type** and configure its settings (size, opacity, etc.).
    *   Click and drag on the 3D model in the viewport to paint.
5.  **Export:** Once you are happy with the result, use the export functionality to save out the generated texture maps.

## Native Dependencies

If you encounter a `DllNotFoundException` or similar issues related to `libSkiaSharp.so` or other native binaries when running on a different OS, you may need to install the appropriate native assets package for your platform. For example, for a generic Linux distribution:

```bash
dotnet add package SkiaSharp.NativeAssets.Linux.NoDependencies
```

Replace `Linux` with your target platform (e.g., `Windows`, `macOS`). The correct package for macOS was likely installed automatically as a dependency.