#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): converts getdata.ly's output into
Data/LilyPondData.g.cs.

Usage:
  1. From the CodeBrix.LilyPort directory, run getdata.ly through the engine:
       dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
           <this directory> /tmp/datagen-out > /tmp/datagen.log 2>&1
  2. python3 gen-lilypond-data.py /tmp/datagen.log > \
       ../../src/libs/Fresco.Brix.Ly/Data/LilyPondData.g.cs

The module text between the sentinels is exec'd as Python (it is python-ly's
own generated-module format) and re-emitted as C#.
"""
import sys


def cs_str(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def wrap_list(items, indent):
    lines = []
    line = ' ' * indent
    for s in items:
        piece = ' ' + cs_str(s) + ','
        if len(line) + len(piece) > 96:
            lines.append(line)
            line = ' ' * indent
        line += piece
    if line.strip():
        lines.append(line)
    return lines


def emit_dict(name, doc, data):
    print(f'    /// <summary>{doc}</summary>')
    print(f'    internal static readonly Dictionary<string, string[]> {name} = new Dictionary<string, string[]>')
    print('    {')
    for key, value in data.items():
        items = ', '.join(cs_str(s) for s in value)
        entry = f'        {{ {cs_str(key)}, [{items}] }},'
        if len(entry) <= 100:
            print(entry)
        else:
            print(f'        {{')
            print(f'            {cs_str(key)},')
            print(f'            [')
            for line in wrap_list(value, 15):
                print(line)
            print(f'            ]')
            print(f'        }},')
    print('    };')
    print()


def emit_array(name, doc, data):
    print(f'    /// <summary>{doc}</summary>')
    print(f'    internal static readonly string[] {name} =')
    print('    [')
    for line in wrap_list(data, 7):
        print(line)
    print('    ];')
    print()


def main():
    log = open(sys.argv[1]).read()
    begin = log.index('###BEGIN-DATA###') + len('###BEGIN-DATA###')
    end = log.index('###END-DATA###')
    module = log[begin:end]

    scope = {}
    exec(module, scope)

    print('''// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System.Collections.Generic;

namespace Fresco.Brix.Ly.Data; //was previously: ly/data/_lilypond_data.py (REGENERATED);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
// REGENERATED from the CodeBrix.LilyPort 2.27.2 engine by tools/datagen
// (getdata.ly through BatchDriver, converted by gen-lilypond-data.py) —
// NOT ported from python-ly's stale 2.24.0 data (the plan's W1d).
// Edit the generator, never this file.

/// <summary>The LilyPond-introspected data: grob interfaces and their
/// properties, grob-to-interface mapping, context properties, translators, and
/// the Emmentaler glyph list — from the engine this application embeds.</summary>
internal static class LilyPondData
{''')

    # The engine version is NOT written into the generated C# as a literal.
    # Fresco.Brix names the LilyPond release in exactly ONE C# place — LilyPort's
    # LilyVersion.CompatibleWithVersion — and the host injects it into
    # LyData.Version at startup. The value is recorded in the header comment above
    # so a reader can still see which engine this data came from.
    emit_dict('Interfaces', 'Interface name to its user-property names.', scope['interfaces'])
    emit_dict('Grobs', 'Grob name to the interfaces it implements.', scope['grobs'])
    emit_array('Contextproperties', 'All user translation properties.', scope['contextproperties'])
    emit_array('Engravers', 'All engravers and performers.', scope['engravers'])
    emit_array('Musicglyphs', 'The Emmentaler glyph names.', scope['musicglyphs'])
    print('}')


if __name__ == '__main__':
    main()
