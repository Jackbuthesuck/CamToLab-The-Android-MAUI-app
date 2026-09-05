# CamtoLab

A simple .NET MAUI app for capturing a photo and sampling colors from the camera. Or file picker, that's also an option now.

## What it does

- Use your phone camera
- sample a single pixel or an average area
- show RGB and Lab values
- set white standard
- get Delta E and Delta C between custom point A and B
- switch between Single Pixel and Average Area sampling
- preview the active sample as a live title-bar color
- compare saved A and B samples with persistent visual markers
- load a built-in PNG test card without needing a camera
- display images with aspect-fit selection coordinates, including landscape images

## Requirements

- Visual Studio 2026 with the .NET MAUI workload
- .NET 10 SDK
- Android SDK or emulator if running on Android
- Camera permission enabled

## Run it

```bash
dotnet restore
dotnet build
```

Then open the solution in Visual Studio and run the `CamtoLab` project on your device or emulator. Android is the primary target as I don't have an apple phone; the project also contains the standard .NET MAUI platform targets.

## Usage

This thing is simple enough, I'm sure you'll figure it out. But just in case...:

1. Launch the app. The camera preview starts when the page appears.
2. Tap **Take Photo** to capture an image, or open the flyout and choose **Pick Image**.
3. Drag the selection to the color you want to inspect.
4. Use **Single Pixel** for one pixel or **Average Area** for the selected square. Areas touching an image edge are clipped to the actual image.
5. Use **Set as White Standard** to calibrate Lab values. The calibration remains active when changing images during the current app session, but resets when the app restarts. Its marker belongs only to the image where it was set.
6. Use **Set A** and **Set B** to save comparison samples. Each marker remembers whether it was a pixel or area sample, even if the current mode later changes.
7. Use the display switch in the flyout to choose RGB or Lab output. RGB is the default.

The title bar changes to the current sampled color.

## Origin

I have nothing going on and my mate think this would be a cool thing to have, and so I did it.