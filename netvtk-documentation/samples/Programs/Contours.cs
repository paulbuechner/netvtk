// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#region program
using System;
using Vtk;

namespace Contours;

// a sphere's height as a field on its surface, cut into rings of equal height, written as .vtp
internal static class Program
{
  private static void Main()
  {
    using var sphere = new vtkSphereSource();
    sphere.SetThetaResolution(64);
    sphere.SetPhiResolution(32);

    // each point's height as the field "Elevation": 0 at the bottom, 1 at the top
    using var elevation = new vtkElevationFilter();
    elevation.SetLowPoint(0, 0, -0.5);
    elevation.SetHighPoint(0, 0, 0.5);
    using var spherePort = sphere.GetOutputPort();
    elevation.SetInputConnection(spherePort);

    // nine rings, where the field is 0.1, 0.2, ..., 0.9
    using var contours = new vtkContourFilter();
    using var elevationPort = elevation.GetOutputPort();
    contours.SetInputConnection(elevationPort);
    contours.GenerateValues(9, 0.1, 0.9);

    // Update runs the pipeline up to the contours
    contours.Update();
    using var rings = contours.GetOutput();
    Console.WriteLine($"{rings.GetNumberOfPoints()} points, {rings.GetNumberOfLines()} segments");

    using var writer = new vtkXMLPolyDataWriter();
    writer.SetFileName("rings.vtp");
    writer.SetInputData(rings);
    writer.Write();
  }
}
#endregion
