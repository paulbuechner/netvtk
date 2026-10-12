// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

//
using Vtk;

namespace NetVtk.Viewer.Tests;

/// <summary>What a render window shows, a pixel at a time (its back buffer, as rendered).</summary>
internal static class Pixels
{
  public static (int R, int G, int B)[] Of(vtkRenderWindow window)
  {
    using var grab = new vtkWindowToImageFilter();
    grab.SetInput(window);
    grab.SetInputBufferTypeToRGB();
    grab.ReadFrontBufferOff();
    grab.Update();
    using var image = grab.GetOutput();
    using var data = image.GetPointData();
    using var scalars = data.GetScalars();
    using var colors = vtkUnsignedCharArray.SafeDownCast(scalars);
    var rgb = colors.ToArray();
    return
    [
      .. Enumerable.Range(0, rgb.Length / 3)
        .Select(i => ((int)rgb[3 * i], (int)rgb[3 * i + 1], (int)rgb[3 * i + 2]))
    ];
  }
}
