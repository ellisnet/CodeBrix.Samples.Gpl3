#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): records how PYTHON changes the case of
a string, because three of FD10's native commands are Frescobaldi snippets whose
entire body is `text.upper()`, `text.lower()` or `text.title()`, and Python's
answers are not .NET's.

Three differences matter and all three are visible in the fixture:

  * Python applies Unicode's FULL case mappings, so a mapping may grow the
    string ('straße'.upper() == 'STRASSE', 'ﬁ'.upper() == 'FI'); .NET's
    invariant casing is the SIMPLE 1:1 mapping and leaves both alone.
  * Python's lower() applies the Greek FINAL SIGMA rule in context
    ('ΑΣ'.lower() == 'ας' with U+03C2, not U+03C3).
  * Python's title() walks the string tracking whether the PREVIOUS character
    was cased, titlecasing the character after an uncased one and lowercasing
    the rest -- which is why "they're".title() is "They'Re" and 'de5f'.title()
    is 'De5F'.  Its per-character mapping is the TITLECASE one, which for the
    58 Lt characters is not the uppercase one ('ǆ' titles to 'ǅ', not 'Ǆ').

Usage:
    python3 tools/snippetprobe/gen-case-tables.py \
        src/Fresco.Brix.Core/Tools/PythonCase.g.cs \
        tests/Fresco.Brix.Core.Tests/fixtures/python-case.txt

The tables are the character-set FACTS of the Unicode Character Database as
Python's own `str` methods answer them -- the same standing as the decoding
tables `tools/charsettables/` reads out of Python's codec registry.  The sweep
fixture is the proof: it names every code point in Unicode whose case differs
from itself, and the port's tests reproduce all three mappings over the lot.
"""
import sys


def cased(codepoint):
    """Python's own Cased property, read off its own title() behaviour.

    A character after a CASED character is lowercased by title(); one after an
    UNCASED character is titlecased.  Appending a lower-case 'a' therefore asks
    Python the question directly.
    """
    return (chr(codepoint) + 'a').title().endswith('a')


def case_ignorable(codepoint):
    """Python's own Case_Ignorable property, read off its FINAL SIGMA rule.

    Lowercasing a capital sigma answers U+03C2 when the nearest character
    before it that is not case-ignorable is CASED (and nothing cased follows).
    Putting the candidate between a cased letter and the sigma therefore asks
    Python whether it skipped the candidate -- true for a cased character too,
    which `cased()` above subtracts out.
    """
    lowered = ('Α' + chr(codepoint) + 'Σ').lower()
    return lowered.endswith('ς') and not cased(codepoint)


def ranges(values):
    """Collapses a sorted list of code points into (first, last) ranges."""
    out = []
    for value in values:
        if out and out[-1][1] == value - 1:
            out[-1][1] = value
        else:
            out.append([value, value])
    return out


def literal(text):
    """A C# string literal holding one \\uXXXX escape per UTF-16 code unit."""
    units = text.encode('utf-16-le')
    return '"' + ''.join(
        '\\u{0:04x}'.format(units[i] | (units[i + 1] << 8))
        for i in range(0, len(units), 2)) + '"'


HEADER = '''// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// GENERATED FILE - do not edit by hand.
// Regenerate with: python3 tools/snippetprobe/gen-case-tables.py
//
// NOT third-party content: the case mappings of the Unicode Character
// Database, read out of Python's own str methods by tools/snippetprobe/,
// because three of the commands FD10 makes native are Frescobaldi snippets
// whose whole body is one of those methods. The mappings are facts, not
// authorship - the same standing as Editor/CharsetTables.g.cs.

using System.Collections.Generic;

namespace Fresco.Brix.Tools;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

public static partial class PythonCase
{
'''


def main():
    if len(sys.argv) < 3:
        sys.stderr.write(__doc__)
        return 2

    full_upper = {}
    full_lower = {}
    title_map = {}
    cased_points = []
    ignorable_points = []

    for codepoint in range(0x110000):
        char = chr(codepoint)
        upper = char.upper()
        lower = char.lower()
        title = char.title()
        #EVERY mapping that is not the identity is tabled, rather than only the
        #ones .NET gets wrong: .NET's simple mapping disagrees with Python's in
        #places (U+0131 DOTLESS I uppercases to I in Python and to itself in
        #.NET), and which places is an ICU version's business, not a fact about
        #Python. Reading the whole answer out of Python leaves nothing to drift.
        if upper != char:
            full_upper[codepoint] = upper
        if lower != char:
            full_lower[codepoint] = lower
        if title != char:
            title_map[codepoint] = title
        if cased(codepoint):
            cased_points.append(codepoint)
        elif case_ignorable(codepoint):
            ignorable_points.append(codepoint)

    lines = [HEADER]

    def table(name, mapping, summary):
        lines.append('    /// <summary>{0}</summary>'.format(summary))
        lines.append('    public static readonly IReadOnlyDictionary<int, string> '
                     '{0}'.format(name))
        lines.append('        = new Dictionary<int, string>')
        lines.append('        {')
        for codepoint in sorted(mapping):
            lines.append('            [0x{0:04X}] = {1},'.format(
                codepoint, literal(mapping[codepoint])))
        lines.append('        };')
        lines.append('')

    table('FullUpper', full_upper,
          'Every code point whose Python uppercase is not itself.')
    table('FullLower', full_lower,
          'Every code point whose Python lowercase is not itself.')
    table('TitleMap', title_map,
          'Every code point whose Python titlecase is not itself.')

    def range_table(name, points, summary):
        lines.append('    /// <summary>{0}</summary>'.format(summary))
        lines.append('    public static readonly int[] {0} ='.format(name))
        lines.append('    {')
        for first, last in ranges(points):
            lines.append('        0x{0:04X}, 0x{1:04X},'.format(first, last))
        lines.append('    };')
        lines.append('')

    range_table('CasedRanges', cased_points,
                'The Cased code points, as first/last pairs.')
    range_table(
        'CaseIgnorableRanges', ignorable_points,
        'The Case_Ignorable code points the final-sigma rule skips, as '
        'first/last pairs.')
    while lines and lines[-1] == '':
        lines.pop()
    lines.append('}')

    with open(sys.argv[1], 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')

    #-- the sweep fixture -------------------------------------------------
    fixture = [
        '# Fresco.Brix parity fixture: Python\'s own case mappings, code point',
        '# by code point. GENERATED by tools/snippetprobe/gen-case-tables.py.',
        '# Never hand-edit, and never regenerate from the port.',
        '#',
        '# One code point per line, tab-separated, hexadecimal:',
        '#   codepoint <TAB> upper <TAB> lower <TAB> title <TAB> cased',
        '# Only the code points where at least one mapping is not the code',
        '# point itself are listed; "cased" is 1 or 0.',
        '#',
    ]
    for codepoint in range(0x110000):
        char = chr(codepoint)
        upper, lower, title = char.upper(), char.lower(), char.title()
        if upper == char and lower == char and title == char:
            continue
        fixture.append('\t'.join([
            '{0:04X}'.format(codepoint),
            ' '.join('{0:04X}'.format(ord(c)) for c in upper),
            ' '.join('{0:04X}'.format(ord(c)) for c in lower),
            ' '.join('{0:04X}'.format(ord(c)) for c in title),
            '1' if cased(codepoint) else '0']))

    with open(sys.argv[2], 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(fixture) + '\n')

    sys.stderr.write(
        'tables: {0} full-upper, {1} full-lower, {2} title, {3} cased ranges; '
        'fixture: {4} code points\n'.format(
            len(full_upper), len(full_lower), len(title_map),
            len(ranges(cased_points)), len(fixture) - 9))
    return 0


if __name__ == '__main__':
    sys.exit(main())
