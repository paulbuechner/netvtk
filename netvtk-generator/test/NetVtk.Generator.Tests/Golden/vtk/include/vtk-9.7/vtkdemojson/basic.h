// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a third-party library's template in its own directory, as VTK installs its copies (vtknlohmannjson):
// a member using an instance of it is skipped, the instance isn't wrapped.
#pragma once

namespace vtkdemojson
{
template <class T>
class basic
{
public:
  T value;
};
}
