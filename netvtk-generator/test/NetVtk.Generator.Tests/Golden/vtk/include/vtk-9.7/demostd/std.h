// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: the standard library's vector and string as libclang spells them (std::vector<T, std::allocator<T>>,
// std::basic_string<char, ...>), so that the fixture needs none.
#pragma once

namespace std
{
template <class T>
class allocator
{
};

template <class C>
class char_traits
{
};

template <class C, class T = char_traits<C>, class A = allocator<C>>
class basic_string
{
};

typedef basic_string<char> string;

template <class T, class A = allocator<T>>
class vector
{
};
}
