#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs Frescobaldi's OWN document-variable
scanner over a set of probe documents and writes the results as fixtures the
Fresco.Brix.Core.Tests parity test replays.

frescobaldi/variables.py imports PyQt6 at module level (for the update timer),
and PyQt6 is not installed here — nor may it be. The scanner itself is pure
python, so this tool lifts exactly the pure definitions out of the READ-ONLY
checkout and executes them: _LINES, _variable_re, positions(), variables() and
prepare(). Nothing is retyped, so the fixtures really are upstream's answers.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-variables-fixtures.py <out-dir>
"""
import ast
import json
import os
import re
import sys

SOURCE = os.path.expanduser('~/GitHome/frescobaldi/frescobaldi/variables.py')

# The pure, PyQt-free names the scanner is made of.
WANTED = ('_LINES', '_variable_re', 'positions', 'variables', 'prepare')


def load_upstream():
    """Execute just the pure parts of upstream's variables.py."""
    with open(SOURCE, encoding='utf-8') as handle:
        source = handle.read()
    tree = ast.parse(source)
    kept = []
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef,)) and node.name in WANTED:
            kept.append(node)
        elif isinstance(node, ast.Assign):
            names = [t.id for t in node.targets if isinstance(t, ast.Name)]
            if any(n in WANTED for n in names):
                kept.append(node)
    module = ast.Module(body=kept, type_ignores=[])
    namespace = {'re': re}
    exec(compile(module, SOURCE, 'exec'), namespace)   # noqa: S102 - upstream's own code
    missing = [n for n in WANTED if n not in namespace]
    if missing:
        raise SystemExit('could not lift {0} out of variables.py'.format(missing))
    return namespace


PROBES = {
    'simple': "% -*- indent-width: 4; coding: utf-8; -*-\n"
              "\\relative c' { c d e }\n",
    'no-marker': "% indent-width: 4;\n\\relative c' { c d e }\n",
    'continuation': "% -*- indent-width: 4;\n% coding: latin1;\n"
                    "\\relative c' { c }\n",
    'interrupted': "% -*- indent-width: 4;\n\\relative c' { c }\n"
                   "% coding: latin1;\n",
    'tail-only': "\n".join(['c{0}'.format(n) for n in range(1, 21)]
                           + ['% -*- indent-width: 8; -*-']),
    'middle-ignored': "\n".join(['c{0}'.format(n) for n in range(1, 11)]
                                + ['% -*- indent-width: 8; -*-']
                                + ['c{0}'.format(n) for n in range(11, 21)]),
    'both-ends': "\n".join(['% -*- indent-width: 2; -*-']
                           + ['c{0}'.format(n) for n in range(1, 21)]
                           + ['% -*- indent-width: 6; -*-']),
    'many-on-one-line': "%% -*- indent-tabs: no; indent-width: 3; tab-width: 4; -*-\n",
    'exactly-ten-lines': "\n".join(['c{0}'.format(n) for n in range(1, 6)]
                                   + ['% -*- indent-width: 5; -*-']
                                   + ['c{0}'.format(n) for n in range(7, 11)]),
    'eleven-lines': "\n".join(['c{0}'.format(n) for n in range(1, 6)]
                              + ['% -*- indent-width: 5; -*-']
                              + ['c{0}'.format(n) for n in range(7, 12)]),
    'no-comment-prefix': "-*- indent-width: 7; -*-\nc\n",
    'marker-mid-line': "\\version \"2.27.2\"  % -*- indent-width: 9; -*-\nc\n",
    'empty': "",
    'blank-value': "% -*- coding: ; -*-\nc\n",
    'hyphenated-name': "% -*- document-tab-width: 12; -*-\nc\n",
}


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    upstream = load_upstream()
    result = {}
    for name, text in sorted(PROBES.items()):
        result[name] = {
            'text': text,
            'variables': upstream['variables'](text),
        }
        print('{0}: {1}'.format(name, result[name]['variables']))

    with open(os.path.join(out_dir, 'variables.json'), 'w', encoding='utf-8') as out:
        json.dump(result, out, indent=2, sort_keys=True)
    print('TOTAL {0} probes'.format(len(result)))


if __name__ == '__main__':
    main()
