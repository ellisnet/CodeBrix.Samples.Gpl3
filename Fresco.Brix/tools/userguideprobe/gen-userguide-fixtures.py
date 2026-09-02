#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): records what Frescobaldi's OWN
`simplemarkdown.py' and `userguide/' package produce, so the C# port is
verified against the original rather than against itself.

Two fixtures come out of here.

  simplemarkdown.json   The PURE parser. `simplemarkdown.py' imports nothing
                        but contextlib, so board trap 49 applies -- an oracle
                        that imports clean needs no shim at all. For every one
                        of the 80 user-guide page files, and for a set of
                        hand-written snippets that exercise the corners the
                        pages do not reach, this records the parse TREE (the
                        module's own `Tree.dump()') and the HTML the module's
                        own `HtmlOutput' writes.

  userguide.json        The USER GUIDE on top of it: `read.split_document()'s
                        #-block split, `read.Parser''s inline-text handling
                        (the "!" no-translate prefix and the "_(" ... ")_"
                        islands inside it), `page.Page''s title/children/
                        seealso, and the body HTML with every {variable}
                        resolved by `page.Resolver'.

`userguide/page.py' imports PyQt6, so board trap 46 applies to the second
fixture: `tools/scorewizprobe/qtshim.py' is REUSED unchanged (it is a module,
and the score wizard's fixtures must stay unaffected), with the few extra
stand-ins this package needs installed on top of it here. PyQt6 is not
installed on this machine and must not be (board rule 11).

Three of the resolver's answers cannot be recorded faithfully, because they
read a running Frescobaldi rather than a file:

  shortcut          upstream asks its own action collections. Recorded as the
                    sentinel below; the C# test substitutes the port's answer.
  {appname} {version} {author}
                    upstream's `appinfo'. Recorded as upstream's own values;
                    the C# test substitutes Fresco.Brix's.
  lilypond code     upstream colorizes ```lilypond blocks through
                    `highlight2html', which drags in `ly.colorize' and the
                    editor's colour scheme. Recorded as the PLAIN <code><pre>
                    form, which is what this port keeps in HTML (it colorizes
                    when it draws instead).

Everything else -- the menu paths, the URLs, the images, the language names,
the plain and markdown and html variables, the whole table of contents -- is
upstream's own code running over upstream's own data.

Usage:
    python3 tools/userguideprobe/gen-userguide-fixtures.py \
        --frescobaldi ~/GitHome/frescobaldi \
        --out tests/Fresco.Brix.Core.Tests/fixtures/userguide
"""
import argparse
import json
import os
import sys
import types


#What a resolved shortcut is recorded as. The C# test replaces this with the
#port's own answer for the named action before comparing.
SHORTCUT_SENTINEL = '\x01SHORTCUT:{0} {1}\x01'

#What a ```lilypond block is recorded as. Upstream colorizes it through
#`highlight2html', which reads the editor's colour scheme -- a running window,
#not a file. This port colorizes when it DRAWS the block instead, and the HTML
#it keeps is the PLAIN <code><pre> form, so the stand-in produces exactly that:
#the fixture then records the plain form and the comparison needs no
#substitution at all.
def lilypond_html(code, full_html=True):
    escaped = (code.replace('&', '&amp;').replace('<', '&lt;')
               .replace('>', '&gt;'))
    return '<code><pre>' + escaped + '</pre></code>'


def install_shim(frescobaldi_path):
    """Installs the PyQt6/Frescobaldi stand-ins and puts Frescobaldi on the path.

    Returns nothing; afterwards `import simplemarkdown' and `import userguide'
    reach the REAL modules.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    sys.path.insert(0, os.path.join(os.path.dirname(here), 'scorewizprobe'))
    import qtshim

    #python-ly is not needed here; the shim only wants a path it can prepend.
    qtshim.install(frescobaldi_path, frescobaldi_path)

    #The shim registers a STUB `userguide' (the score wizard's parts call
    #userguide.addButton). This probe wants the real package, so take the stub
    #back out -- qtshim itself is left untouched.
    sys.modules.pop('userguide', None)

    #...and the real `language_names', whose data file is pure Python.
    sys.modules.pop('language_names', None)

    def _module(name, **members):
        module = types.ModuleType(name)
        for key, value in members.items():
            setattr(module, key, value)
        sys.modules[name] = module
        return module

    class _PrioritySignal:
        """`app.languageChanged' -- whose connect() takes a priority."""

        def __init__(self):
            self._slots = []

        def connect(self, slot, priority=0):
            self._slots.append(slot)

        def emit(self, *args):
            for slot in list(self._slots):
                slot(*args)

    app = sys.modules['app']
    app.languageChanged = _PrioritySignal()
    app.settingsChanged = _PrioritySignal()

    #`read.Parser.translate' asks i18n for a "userguide" translator and catches
    #i18n.UnknownLanguageError. The fixture is recorded in ENGLISH, so the
    #translator is the identity -- which is exactly what Frescobaldi itself
    #does when the current language has no catalog.
    i18n = sys.modules['i18n']
    i18n.UnknownLanguageError = type('UnknownLanguageError', (Exception,), {})
    i18n.translator = lambda language, domain=None: (lambda message: message)

    #qutil's real removeAccelerator (qutil.py:157) -- handle_menu leans on it.
    def remove_accelerator(text):
        return text.replace('&&', '\0').replace('&', '').replace('\0', '&')

    sys.modules['qutil'].removeAccelerator = remove_accelerator

    #`handle_shortcut' reads QKeySequence.SequenceFormat.NativeText. qtshim's
    #QKeySequence is a plain lambda (the score wizard never touches the enum),
    #so give it the one attribute this code path asks for.
    qtgui = sys.modules['PyQt6.QtGui']
    qtgui.QKeySequence = type(
        'QKeySequence', (),
        {'SequenceFormat': qtshim._Enum('QKeySequence.SequenceFormat'),
         'StandardKey': qtshim._Enum('QKeySequence.StandardKey')})

    _module('highlight2html', html_text=lilypond_html)

    class _Action:
        def __init__(self, collection, name):
            self._collection = collection
            self._name = name

        def shortcut(self):
            return _Sequence(self._collection, self._name)

    class _Sequence:
        def __init__(self, collection, name):
            self._collection = collection
            self._name = name

        def toString(self, _format=None):
            return SHORTCUT_SENTINEL.format(self._collection, self._name)

    _module('actioncollectionmanager',
            action=lambda collection, name: _Action(collection, name))


def page_names(userguide_dir):
    """Every page in the userguide directory, sorted."""
    return sorted(
        name[:-3] for name in os.listdir(userguide_dir) if name.endswith('.md'))


def snippets():
    """Hand-written markdown exercising what the pages do not reach.

    Every one of these is a corner of `simplemarkdown.Parser' that the 80
    shipped pages leave untested: an ordered list of several items (the pages
    only ever have one), a list nested three deep, a definition list with a
    multi-line definition, an unterminated link, an emphasis that spans a
    newline, a code fence with no specifier, and an empty document.
    """
    return {
        'empty': '',
        'blank_lines': '\n\n\n',
        'heading_one': '=== A title ===',
        'heading_two': '== A sub-title ==',
        'heading_three': '= A sub-sub-title =',
        'heading_four_signs': '==== Four signs ====',
        'heading_unclosed': '=== No closing signs',
        'paragraph': 'One line\nand another line.',
        'two_paragraphs': 'First paragraph.\n\nSecond paragraph.',
        'ul_single': '* only item',
        'ul_many': '* one\n* two\n* three',
        'ol_single': '1. only item',
        'ol_many': '1. one\n2. two\n3. three',
        'ol_not_a_number': 'a. not an item',
        'nested_ul_in_ul': '* one\n  * nested\n  * nested two\n* two',
        'nested_ol_in_ul': '* one\n  1. nested\n  2. nested two',
        'nested_three_deep':
            '* one\n  * two\n    * three\n\n      a paragraph at three',
        'list_then_paragraph': '* one\n* two\n\nA following paragraph.',
        'dl_simple': 'term\n: definition',
        'dl_multiline': 'term\n: definition first line\n  and second line',
        'dl_two_items': 'one\n: first\n\ntwo\n: second',
        'code_plain': '```\nverbatim & <code>\n```',
        'code_lilypond': '```lilypond\n\\relative c\' { c d e }\n```',
        'code_indented': '  ```\n  indented code\n  ```',
        'code_unterminated': '```\nno end fence',
        'inline_emphasis': 'a *bold* word',
        'inline_emphasis_unclosed': 'a *bold word',
        'inline_emphasis_over_newline': 'a *bold\nword* here',
        'inline_code': 'a `literal` word',
        'inline_code_unclosed': 'a `literal word',
        'inline_code_with_markup': 'a `*not emphasis*` word',
        'link_bare': 'see [http://example.org/] please',
        'link_with_text': 'see [http://example.org/ the example] please',
        'link_internal': 'see [somepage the page name] please',
        'link_unterminated': 'see [http://example.org/ dangling',
        'link_with_emphasis': 'see [http://example.org/ the *example*] please',
        'escapes': 'less < greater > amp & done',
        'variable_only': '{justavariable}',
        'variable_in_text': 'See {somewhere} for more.',
        'bang_prefix': '!not translated {var} at all',
        'bang_with_islands': '!`-dno-point-and-click`: _(No point and click)_',
        'star_in_word': '2*3 is not emphasis',
        'brackets_in_code': 'a `[bracket]` here',
    }


def read_document(userguide_dir, name):
    with open(os.path.join(userguide_dir, name + '.md'), 'rb') as handle:
        return handle.read().decode('utf-8')


def build_simplemarkdown(userguide_dir, names):
    """The pure-parser fixture: tree dump and HTML for pages and snippets."""
    import simplemarkdown

    def record(text):
        tree = simplemarkdown.Tree()
        parser = simplemarkdown.Parser()
        parser.parse(text, tree)
        return {
            'tree': tree.dump(),
            'html': simplemarkdown.html(text),
            'text': tree.text(tree.root()),
        }

    return {
        'note': 'Recorded from Frescobaldi simplemarkdown.py by '
                'tools/userguideprobe/gen-userguide-fixtures.py. '
                'The parser is upstream\'s; nothing here is this port\'s output.',
        'snippets': {name: dict(record(text), source=text)
                     for name, text in sorted(snippets().items())},
        'pages': {name: dict(record(read_document(userguide_dir, name)),
                             source=read_document(userguide_dir, name))
                  for name in names},
        'inline': {name: simplemarkdown.html_inline(text)
                   for name, text in sorted({
                       'plain': 'plain text',
                       'emphasis': '*emphasized*',
                       'code': '`code`',
                       'link': '[http://example.org/ text]',
                       'backtick_pair': 'a `b` c `d` e',
                       'no_url': '[bare]',
                   }.items())},
    }


def build_userguide(userguide_dir, names):
    """The user-guide fixture: blocks, tree, title, children, seealso, body."""
    import simplemarkdown
    from userguide import read as ug_read
    from userguide import page as ug_page
    from userguide import util as ug_util

    #md2pot's own trick, and the reason read.Parser.translate is a method:
    #the fixture is recorded in ENGLISH, so translation is the identity.
    class Parser(ug_read.Parser):
        def translate(self, text):
            return text

    pages = {}
    for name in names:
        doc, attrs = ug_read.document(os.path.join(userguide_dir, name + '.md'))
        tree = simplemarkdown.Tree()
        Parser().parse(doc, tree)

        p = ug_page.Page()
        p._name = name
        local_attrs = dict(attrs)
        local_attrs.setdefault('VARS', [])
        local_attrs['VARS'] = list(local_attrs['VARS']) + [
            f'userguide_page md `{name}`']
        p._attrs = local_attrs
        p._tree = tree

        pages[name] = {
            'source': read_document(userguide_dir, name),
            'blocks': {key: list(value) for key, value in sorted(attrs.items())},
            'document': doc,
            'tree': tree.dump(),
            'title': p.title(),
            'children': list(p.children()),
            'seealso': list(p.seealso()),
            'is_popup': p.is_popup(),
            'body': p.body(),
            'link': ug_util.format_link(name),
        }

    #The three resolver answers that are NOT read from a page file.
    resolve_functions = {}
    from userguide import resolve as ug_resolve
    for function in ('appname', 'version', 'author'):
        resolve_functions[function] = getattr(ug_resolve, function)()

    #...and the language names, which upstream reads from its own 3,573-line
    #`language_names' data package (which arrives with W-I18N). Recorded for
    #every code the corpus asks for, so the parity test can hand the port
    #upstream's own answers and compare the credits page exactly.
    import language_names
    codes = set()
    for name in names:
        _, attrs = ug_read.document(os.path.join(userguide_dir, name + '.md'))
        for line in attrs.get('VARS', []):
            parts = line.split(None, 2)
            if len(parts) == 3 and parts[1].lower() == 'languagename':
                codes.add(parts[2])
    language = {code: language_names.languageName(code, 'en')
                for code in sorted(codes)}

    #...and the menu names upstream's handle_menu predefines. Ruling FR13
    #renames one of them (`lilypond' -> the &LilyPort menu), so the port's
    #table and upstream's differ by exactly that row.
    menu_names = {
        'file': 'menu title|&File', 'edit': 'menu title|&Edit',
        'view': 'menu title|&View', 'snippets': 'menu title|Sn&ippets',
        'music': 'menu title|&Music', 'lilypond': 'menu title|&LilyPond',
        'tools': 'menu title|&Tools', 'window': 'menu title|&Window',
        'session': 'menu title|&Session', 'help': 'menu title|&Help',
    }

    return {
        'note': 'Recorded from Frescobaldi userguide/ by '
                'tools/userguideprobe/gen-userguide-fixtures.py. '
                'Shortcuts and colorized LilyPond are sentinels (see the tool).',
        'shortcut_sentinel': SHORTCUT_SENTINEL,
        'resolve_functions': resolve_functions,
        'language_names': language,
        'menu_names': menu_names,
        'pages': pages,
        'table_of_contents': ug_resolve.table_of_contents(),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frescobaldi', required=True,
                        help='the Frescobaldi checkout (read-only)')
    parser.add_argument('--out', required=True,
                        help='the fixture directory to write into')
    args = parser.parse_args()

    frescobaldi_path = os.path.join(
        os.path.expanduser(args.frescobaldi), 'frescobaldi')
    userguide_dir = os.path.join(frescobaldi_path, 'userguide')
    if not os.path.isdir(userguide_dir):
        raise SystemExit(f'not a Frescobaldi checkout: {args.frescobaldi}')

    install_shim(frescobaldi_path)
    names = page_names(userguide_dir)
    print(f'{len(names)} pages')

    out = os.path.expanduser(args.out)
    os.makedirs(out, exist_ok=True)

    for filename, data in (
            ('simplemarkdown.json', build_simplemarkdown(userguide_dir, names)),
            ('userguide.json', build_userguide(userguide_dir, names))):
        path = os.path.join(out, filename)
        with open(path, 'w', encoding='utf-8') as handle:
            json.dump(data, handle, ensure_ascii=False, indent=1, sort_keys=True)
            handle.write('\n')
        print(f'{path}: {os.path.getsize(path)} bytes')


if __name__ == '__main__':
    main()
