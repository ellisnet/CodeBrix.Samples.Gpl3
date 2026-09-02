// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.Hosting;
using Fresco.Brix.Services;
using System;

namespace Fresco.Brix;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        //FD5: a second launch hands its files to the window that is already up
        //and stops here — BEFORE this process reads the settings store for
        //itself, so two processes never share one.
        if (RemoteInstance.TryHandOff(args)) { return; }

        App.CommandLine = CommandLineArguments.Parse(args);
        App.CommandLinePaths = App.CommandLine.Files;

        var host = CodeBrixPlatformHostBuilder.Create()
            .App(() => new App())
            .UseLinuxWayland()
            .UseDirectSkiaCanvasMode() //Experimental - should be safe to leave enabled
            .Build();

        host.Run();
    }
}
