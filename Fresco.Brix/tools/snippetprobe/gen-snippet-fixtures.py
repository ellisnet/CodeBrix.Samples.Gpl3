#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): records what Frescobaldi's twenty-two
PYTHON snippets DO, by running upstream's own bodies over the shim in
`textshim.py`, so that FD10's native commands are verified against UPSTREAM and
never against the port's own reading of it.

Usage:  python3 tools/snippetprobe/gen-snippet-fixtures.py [output.txt]

The oracle is upstream's, three layers deep and unmodified at every one:

  * the snippet BODIES come from `frescobaldi/snippet/builtin.py`, imported (it
    needs no Qt), so the bytes executed are upstream's;
  * `insert_python`'s namespace and its handling of a `text` that comes back a
    string or a list is copied verbatim below from `frescobaldi/snippet/
    insert.py` -- the file itself cannot be imported, because `tokeniter` and
    `indent` pull in the highlighter and Qt proper;
  * `cursortools` is loaded from the checkout unchanged, and `lydocument` is
    python-ly's own document API, which is what Frescobaldi's subclass is.

WHAT IS DELIBERATELY LEFT OUT of the recorded result: the RE-INDENT pass
`insert()` runs afterwards.  It is `indent.re_indent`, whose port (Indenting)
was verified against python-ly at W1 and is shared by the snippet inserter; a
fixture that included it would be testing that verified code twice and would
make every expectation depend on it.  The port's tests therefore run the
command with re-indentation off, exactly as recorded here.

Three snippets are NOT recorded here, each for a stated reason:

  * `remove_matching_pair` -- its whole content is `matcher.matches(cursor)`,
    already ported and verified as `TokenMatcher`; removing the ranges it
    answers is one line.
  * `color_dialog` -- everything but the colour table is a dialog; the table
    IS recorded (the dialog's answer is the case input).
  * the two quote snippets -- `lasptyqu.preferred()` is ported and verified as
    `LanguageQuotes`; the quote marks are the case input.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import textshim  # noqa: E402

textshim.install()

from PyQt6.QtGui import QTextCursor, QTextDocument  # noqa: E402


#-- upstream's own variable parsing (snippets.py lines 42 and 154-178) -------
_variables_re = re.compile(r'\s*?([a-z]+(?:-[a-z]+)*)(?::[ \t]*(.*?))?;')


def parse(text):
    lines = text.split('\n')
    start = 0
    while start < len(lines) and lines[start].startswith('-*- '):
        start += 1
    t = '\n'.join(lines[start:])
    d = dict(m.groups(True)
             for l in lines[:start] for m in _variables_re.finditer(l))
    return t, d


#-- upstream's insert.py, insert_python() and the half of insert() that -----
#-- decides the resulting selection; re_indent deliberately not run.
def run_snippet(text, cursor, namespace_extra):
    namespace = {
        'cursor': QTextCursor(cursor),
        'state': namespace_extra['state'],
        'text': cursor.selection().toPlainText(),
        'view': namespace_extra.get('view'),
        'ANCHOR': 1,
        'CURSOR': 2,
    }
    namespace.update(namespace_extra.get('globals', {}))
    code = compile(text, "<snippet>", "exec")
    exec(code, namespace)
    if 'main' in namespace:
        return namespace['main']()
    result = namespace.get('text', '')
    if isinstance(result, (tuple, list)):
        ANCHOR = namespace.get('ANCHOR', 1)
        CURSOR = namespace.get('CURSOR', 2)
        a, c = -1, -1
        for t in result:
            if t == ANCHOR:
                a = cursor.selectionStart()
            elif t == CURSOR:
                c = cursor.selectionStart()
            else:
                cursor.insertText(t)
        if (a, c) != (-1, -1):
            new = QTextCursor(cursor)
            if a != -1:
                new.setPosition(a)
            if c != -1:
                new.setPosition(
                    c,
                    QTextCursor.MoveMode.KeepAnchor if a != -1
                    else QTextCursor.MoveMode.MoveAnchor)
            return new
    else:
        cursor.insertText(namespace['text'])
    return None


def apply_snippet(name, body, variables, document_text, start, end, extra):
    """Runs one snippet over one document, upstream's insert() around it."""
    document = QTextDocument(document_text)
    cursor = QTextCursor(document)
    cursor.setPosition(start)
    cursor.setPosition(end, QTextCursor.MoveMode.KeepAnchor)

    selection = variables.get('selection', '')
    if 'yes' in selection and not cursor.hasSelection():
        return None
    if 'strip' in selection:
        import cursortools
        cursortools.strip_selection(cursor)

    pos = cursor.selectionStart()
    try:
        new = run_snippet(body, cursor, extra)
    except Exception as error:
        #Upstream shows its "Snippet error" box and changes nothing. A native
        #command has nowhere to put an exception, so the case is recorded as
        #the crash it is and the port declares the divergence.
        return 'RAISES:' + type(error).__name__, -1, -1

    if not new and 'keep' in selection:
        anchor, position = pos, cursor.position()
    elif new:
        anchor, position = new.anchor(), new.position()
    else:
        anchor, position = cursor.anchor(), cursor.position()
    return document.toPlainText(), anchor, position


#-- the shims the four dialog/preference-reading snippets need ---------------
class _Colour:
    def __init__(self, rgb):
        self._rgb = rgb

    def getRgb(self):
        return tuple(self._rgb) + (255,)


def colour_namespace(rgb):
    import types
    module = types.ModuleType('inputdialog')
    module.getColor = lambda view=None: None if rgb is None else _Colour(rgb)
    sys.modules['inputdialog'] = module
    return {}


def quotes_namespace(primary, secondary):
    import collections
    import types
    module = types.ModuleType('lasptyqu')
    pair = collections.namedtuple('QuotePair', 'left right')
    quotes = collections.namedtuple('QuoteSet', 'primary secondary')
    module.preferred = lambda: quotes(pair(*primary), pair(*secondary))
    sys.modules['lasptyqu'] = module
    return {}


#-- the cases ---------------------------------------------------------------
LY = 'music = \\relative c\' {\n  c4 d e f\n}\n'

CASES = [
    #(snippet, label, document, start, end, state, extra)

    #--- case transforms: python's own str methods, which are not .NET's ----
    ('uppercase', 'plain', 'abc def', 0, 7, ['lilypond'], {}),
    ('uppercase', 'sharp-s', 'straße', 0, 6, ['lilypond'], {}),
    ('uppercase', 'ligature', 'aﬁn', 0, 3, ['lilypond'], {}),
    ('uppercase', 'accented', 'élève naïve', 0, 11, ['lilypond'], {}),
    ('uppercase', 'no-selection', 'abc', 1, 1, ['lilypond'], {}),
    ('lowercase', 'plain', 'ABC DEF', 0, 7, ['lilypond'], {}),
    ('lowercase', 'final-sigma', 'ΣΟΦΟΣ', 0, 5, ['lilypond'], {}),
    ('lowercase', 'turkish-I', 'ISTANBUL', 0, 8, ['lilypond'], {}),
    ('titlecase', 'plain', 'hello world', 0, 11, ['lilypond'], {}),
    ('titlecase', 'apostrophe', "they're here", 0, 12, ['lilypond'], {}),
    ('titlecase', 'digits', 'abc4 de5f', 0, 9, ['lilypond'], {}),
    ('titlecase', 'backslash', '\\markup foo', 0, 11, ['lilypond'], {}),
    ('titlecase', 'already-upper', 'ABC def', 0, 7, ['lilypond'], {}),
    ('titlecase', 'sharp-s', 'straße gasse', 0, 12, ['lilypond'], {}),
    ('titlecase', 'hyphen', 'well-known name', 0, 15, ['lilypond'], {}),

    #--- markup lines -------------------------------------------------------
    ('markup_lines_selection', 'in-markup', 'one\ntwo', 0, 7,
     ['lilypond', 'markup'], {}),
    ('markup_lines_selection', 'outside-markup', 'one\ntwo', 0, 7,
     ['lilypond'], {}),
    ('markup_lines_selection', 'single-line', 'hello', 0, 5, ['lilypond'], {}),
    ('markup_lines_selection', 'strips-space', '  one\ntwo  ', 0, 11,
     ['lilypond'], {}),

    #--- state-wrapped inserts ---------------------------------------------
    ('no_tagline', 'in-header', 'x', 1, 1, ['lilypond', 'header'], {}),
    ('no_tagline', 'top-level', 'x', 1, 1, ['lilypond'], {}),
    ('no_barnumbers', 'in-context', 'x', 1, 1, ['lilypond', 'context'], {}),
    ('no_barnumbers', 'in-with', 'x', 1, 1, ['lilypond', 'with'], {}),
    ('no_barnumbers', 'in-layout', 'x', 1, 1, ['lilypond', 'layout'], {}),
    ('no_barnumbers', 'top-level', 'x', 1, 1, ['lilypond'], {}),
    ('paper_a5', 'in-paper', 'x', 1, 1, ['lilypond', 'paper'], {}),
    ('paper_a5', 'top-level', 'x', 1, 1, ['lilypond'], {}),
    ('midi_tempo', 'in-context', 'x', 1, 1, ['lilypond', 'context'], {}),
    ('midi_tempo', 'in-with', 'x', 1, 1, ['lilypond', 'with'], {}),
    ('midi_tempo', 'in-midi', 'x', 1, 1, ['lilypond', 'midi'], {}),
    ('midi_tempo', 'top-level', 'x', 1, 1, ['lilypond'], {}),
    ('staff_size', 'in-music', 'x', 1, 1, ['lilypond', 'music'], {}),
    ('staff_size', 'in-new', 'x', 1, 1, ['lilypond', 'new'], {}),
    ('staff_size', 'in-context', 'x', 1, 1, ['lilypond', 'context'], {}),
    ('staff_size', 'in-with', 'x', 1, 1, ['lilypond', 'with'], {}),
    ('staff_size', 'in-layout', 'x', 1, 1, ['lilypond', 'layout'], {}),
    ('staff_size', 'top-level', 'x', 1, 1, ['lilypond'], {}),

    #--- comment / uncomment ------------------------------------------------
    ('comment', 'ly-empty', 'c4 d\n', 2, 2, ['lilypond'], {}),
    ('comment', 'ly-one-line', 'c4 d e\n', 0, 6, ['lilypond'], {}),
    ('comment', 'ly-one-line-text-after', 'c4 d e f\n', 0, 4, ['lilypond'], {}),
    ('comment', 'ly-multi-line', 'c4 d\ne4 f\n', 0, 9, ['lilypond'], {}),
    ('comment', 'ly-multi-line-trailing-nl', 'c4 d\ne4 f\n', 0, 10,
     ['lilypond'], {}),
    ('comment', 'ly-multi-line-text-after', 'c4 d\ne4 f g\n', 0, 9,
     ['lilypond'], {}),
    ('comment', 'scheme', '(display 1)\n', 0, 11, ['lilypond', 'scheme'], {}),
    ('comment', 'scheme-empty', '(display 1)\n', 3, 3,
     ['lilypond', 'scheme'], {}),
    ('comment', 'html', '<p>hi</p>\n', 0, 9, ['html'], {}),
    ('comment', 'html-empty', '<p>hi</p>\n', 3, 3, ['html'], {}),
    ('comment', 'unknown-state-falls-back', 'c4 d e\n', 0, 6, ['markup'], {}),
    ('comment', 'ly-whitespace-after-is-doubled', 'c4 d   \n', 0, 4,
     ['lilypond'], {}),

    ('uncomment', 'ly-line', '% c4 d\n', 0, 6, ['lilypond'], {}),
    ('uncomment', 'ly-many-percent', '%%% c4 d\n', 0, 8, ['lilypond'], {}),
    ('uncomment', 'ly-indented', '  % c4 d\n', 0, 8, ['lilypond'], {}),
    ('uncomment', 'ly-multi-line', '% c4 d\n% e4 f\n', 0, 13, ['lilypond'], {}),
    ('uncomment', 'ly-block', '%{ c4 d %}\n', 0, 10, ['lilypond'], {}),
    ('uncomment', 'ly-block-tight', '%{c4 d%}\n', 0, 8, ['lilypond'], {}),
    ('uncomment', 'ly-no-selection', '% c4 d\ne4 f\n', 3, 3, ['lilypond'], {}),
    ('uncomment', 'ly-no-selection-second-line', '% c4 d\n% e4 f\n', 9, 9,
     ['lilypond'], {}),
    ('uncomment', 'scheme', '; (display 1)\n', 0, 13,
     ['lilypond', 'scheme'], {}),
    ('uncomment', 'html', '<!-- hi -->\n', 0, 11, ['html'], {}),
    ('uncomment', 'unchanged', 'c4 d\n', 0, 4, ['lilypond'], {}),
    ('uncomment', 'html-empty-raises', '<!-- hi -->\n', 3, 3, ['html'], {}),

    #--- line operations ----------------------------------------------------
    ('removelines', 'one-line', 'a\nb\nc\n', 2, 2, ['lilypond'], {}),
    ('removelines', 'first-line', 'a\nb\nc\n', 0, 0, ['lilypond'], {}),
    ('removelines', 'last-line', 'a\nb\nc', 4, 4, ['lilypond'], {}),
    ('removelines', 'selection-spanning-two', 'a\nb\nc\nd\n', 1, 3,
     ['lilypond'], {}),
    ('removelines', 'selection-whole-line', 'a\nb\nc\n', 2, 4,
     ['lilypond'], {}),
    ('removelines', 'only-line', 'a', 0, 0, ['lilypond'], {}),

    ('double', 'current-line', 'a\nb\nc\n', 2, 2, ['lilypond'], {}),
    ('double', 'last-line-no-newline', 'a\nb', 2, 2, ['lilypond'], {}),
    ('double', 'selection', 'abcdef', 1, 4, ['lilypond'], {}),
    ('double', 'blank-line-walks-back', 'a\n\n\n', 2, 2, ['lilypond'], {}),
    ('double', 'blank-first-line', '\n\n', 0, 0, ['lilypond'], {}),

    #--- blank-line navigation ---------------------------------------------
    ('next_blank_line', 'simple', 'a\nb\n\n\nc\n', 0, 0, ['lilypond'], {}),
    ('next_blank_line', 'from-blank', 'a\n\nb\n\nc\n', 2, 2, ['lilypond'], {}),
    ('next_blank_line', 'none-after', 'a\nb\nc', 0, 0, ['lilypond'], {}),
    ('next_blank_line', 'whitespace-line', 'a\n   \nb\n', 0, 0,
     ['lilypond'], {}),
    ('previous_blank_line', 'simple', 'a\n\n\nb\nc\n', 8, 8, ['lilypond'], {}),
    ('previous_blank_line', 'none-before', 'a\nb\nc', 4, 4, ['lilypond'], {}),
    ('previous_blank_line', 'run-of-blanks', 'a\n\n\n\nb\n', 8, 8,
     ['lilypond'], {}),
    ('next_blank_line_select', 'simple', 'a\nb\n\n\nc\n', 0, 0,
     ['lilypond'], {}),
    ('previous_blank_line_select', 'simple', 'a\n\n\nb\nc\n', 8, 8,
     ['lilypond'], {}),

    #--- last note or chord -------------------------------------------------
    ('last_note', 'note-absolute', '{ c4 d8 }\n', 8, 8, ['lilypond'], {}),
    ('last_note', 'note-with-octave-absolute', "{ c4 d'8 }\n", 9, 9,
     ['lilypond'], {}),
    ('last_note', 'note-relative-drops-octave',
     "\\relative c' { c4 d'8 }\n", 22, 22, ['lilypond'], {}),
    ('last_note', 'chord-absolute', '{ c4 <c e g>8 }\n', 14, 14,
     ['lilypond'], {}),
    ('last_note', 'chord-relative-drops-first-octave',
     "\\relative c' { <c' e g>8 }\n", 25, 25, ['lilypond'], {}),
    ('last_note', 'skips-rests', '{ c4 r8 s4 }\n', 11, 11, ['lilypond'], {}),
    ('last_note', 'space-needed', '{ c4 d8 e}\n', 9, 9, ['lilypond'], {}),
    ('last_note', 'nothing-before', '{ }\n', 2, 2, ['lilypond'], {}),
]

QUOTE_CASES = [
    ('quotes_d', 'with-selection', 'say hello now', 4, 9,
     ('“', '”'), ('‘', '’')),
    ('quotes_d', 'no-selection', 'say  now', 4, 4,
     ('“', '”'), ('‘', '’')),
    ('quotes_s', 'with-selection', 'say hello now', 4, 9,
     ('“', '”'), ('‘', '’')),
    ('quotes_s', 'no-selection', 'say  now', 4, 4,
     ('“', '”'), ('‘', '’')),
]

COLOUR_CASES = [
    ('black', (0, 0, 0)),
    ('white', (255, 255, 255)),
    ('red', (255, 0, 0)),
    ('green', (0, 255, 0)),
    ('blue', (0, 0, 255)),
    ('cyan', (0, 255, 255)),
    ('magenta', (255, 0, 255)),
    ('yellow', (255, 255, 0)),
    ('grey', (128, 128, 128)),
    ('darkred', (128, 0, 0)),
    ('darkgreen', (0, 128, 0)),
    ('darkblue', (0, 0, 128)),
    ('darkcyan', (0, 128, 128)),
    ('darkmagenta', (128, 0, 128)),
    ('darkyellow', (128, 128, 0)),
    ('off-table-1', (12, 34, 56)),
    ('off-table-2', (255, 128, 1)),
    ('off-table-3', (1, 2, 3)),
    ('cancelled', None),
]


#Two snippets edit through the COPY of the cursor that `insert_python` puts in
#the namespace and return nothing, so where the caret ends up afterwards is
#Qt's own cursor-adjustment over the outer cursor, not the snippet's answer.
#Their document result is recorded; their caret is not, and the port leaves the
#caret to the editor's own adjustment for exactly the same reason.
NO_CARET = ('removelines', 'uncomment', 'remove_matching_pair')


def escape(text):
    """One fixture field, on one line."""
    return (text.replace('\\', '\\\\')
                .replace('\n', '\\n')
                .replace('\t', '\\t')
                .replace('\r', '\\r'))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else '-'
    snippets = textshim.builtin_snippets()
    lines = []
    lines.append('# Fresco.Brix parity fixture: '
                 'what Frescobaldi\'s python snippets DO.')
    lines.append('# GENERATED by tools/snippetprobe/gen-snippet-fixtures.py '
                 'from the UPSTREAM')
    lines.append('# snippet bodies in frescobaldi/snippet/builtin.py '
                 '(read-only reference checkout).')
    lines.append('# Never hand-edit, and never regenerate from the port.')
    lines.append('#')
    lines.append('# One case per line, tab-separated:')
    lines.append('#   snippet <TAB> label <TAB> before <TAB> start <TAB> end '
                 '<TAB> state <TAB>')
    lines.append('#   after <TAB> anchor <TAB> position')
    lines.append('# "after" is REFUSED when the snippet declined to run '
                 '(selection: yes, no selection).')
    lines.append('# A caret of "-" means the snippet edits through the '
                 'namespace COPY of the cursor')
    lines.append('# and returns nothing, so where the caret ends up is Qt\'s '
                 'own adjustment and not')
    lines.append('# the snippet\'s answer; only the document result is '
                 'recorded for those.')
    lines.append('# The re-indent pass insert() runs afterwards is '
                 'deliberately NOT applied.')
    lines.append('#')

    for name, label, text, start, end, state, extra in CASES:
        body, variables = parse(snippets[name].text)
        result = apply_snippet(
            name, body, variables, text, start, end,
            {'state': state, 'globals': extra.get('globals', {})})
        if result is None:
            lines.append('\t'.join([
                name, label, escape(text), str(start), str(end),
                ','.join(state), 'REFUSED', '-1', '-1']))
            continue
        after, anchor, position = result
        if isinstance(after, str) and after.startswith('RAISES:'):
            lines.append('\t'.join([
                name, label, escape(text), str(start), str(end),
                ','.join(state), after, '-1', '-1']))
            continue
        if name in NO_CARET:
            anchor = position = '-'
        lines.append('\t'.join([
            name, label, escape(text), str(start), str(end), ','.join(state),
            escape(after), str(anchor), str(position)]))

    for name, label, text, start, end, primary, secondary in QUOTE_CASES:
        body, variables = parse(snippets[name].text)
        quotes_namespace(primary, secondary)
        result = apply_snippet(
            name, body, variables, text, start, end, {'state': ['lilypond']})
        after, anchor, position = result
        lines.append('\t'.join([
            name, label, escape(text), str(start), str(end),
            'primary=' + ''.join(primary) + ';secondary=' + ''.join(secondary),
            escape(after), str(anchor), str(position)]))

    body, variables = parse(snippets['color_dialog'].text)
    for label, rgb in COLOUR_CASES:
        colour_namespace(rgb)
        result = apply_snippet(
            'color_dialog', body, variables, 'x', 1, 1,
            {'state': ['lilypond'], 'view': None})
        after, anchor, position = result
        lines.append('\t'.join([
            'color_dialog', label, 'x', '1', '1',
            'rgb=' + ('none' if rgb is None else ','.join(map(str, rgb))),
            escape(after), str(anchor), str(position)]))

    text = '\n'.join(lines) + '\n'
    if out == '-':
        sys.stdout.write(text)
    else:
        with open(out, 'w', encoding='utf-8') as handle:
            handle.write(text)
        sys.stderr.write('wrote {0} cases to {1}\n'.format(
            len(lines) - 11, out))


if __name__ == '__main__':
    main()
