#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): records what Frescobaldi's OWN
`file_import' package decides, so the C# port is verified against the original
rather than against itself.

WHAT IS AND IS NOT RECORDED HERE

The CONVERTERS are not upstream's to record: `musicxml2ly', `midi2ly' and
`abc2ly' are external scripts here and are `CodeBrix.LilyPort.Importers' in
Fresco.Brix, already verified on the LilyPort board against the same corpora.
What IS upstream's, and what this records, is the ADAPTATION LAYER:

  * which file extension picks which importer (`FileImport.targets' and
    `is_importable'), and the file-dialog filter strings;
  * every option dialog's checkboxes -- their order, their settings keys
    (Qt object names) and their defaults;
  * ⚠ THE MAPPING ITSELF: for every combination of checkbox states, the exact
    argument list `configure_job()' hands the converter. This is the part with
    the inverted senses in it ("Import beaming" UNCHECKED adds `--no-beaming'),
    and it is recorded from upstream's own code rather than read off it by eye;
  * the "After Import" tab's four settings and their defaults, in order;
  * `util.next_file', which decides the name the imported document is written
    under when one is already taken.

`file_import' is Qt-widget-shaped, so BOARD TRAP 46 applies:
`tools/scorewizprobe/qtshim.py' is REUSED UNCHANGED (it is a module) and the
few stand-ins this package needs are installed on top of it here. PyQt6 is not
installed on this machine and must not be (board rule 11).

Three stand-ins carry real weight and are named so a reader can check them:

  job.Job           upstream's external-command wrapper. Here it RECORDS the
                    arguments instead of building a command line, which is the
                    whole point of the fixture. Ruling FD1 drops the external
                    half; the arguments are what survives.
  lilychooser       upstream's LilyPond-version chooser, which ruling FR5.1
                    kills. It answers one fixed version, so `configure_job'
                    reaches its argument-adding code.
  QSettings         qtshim's always-answers-the-default one is replaced by a
                    recording dictionary, so `loadSettings' and `saveSettings'
                    can be round-tripped.

Usage:
    python3 tools/importprobe/gen-import-fixtures.py \
        --frescobaldi ~/GitHome/frescobaldi \
        --out tests/Fresco.Brix.Core.Tests/fixtures/import
"""
import argparse
import itertools
import json
import os
import sys
import types


def lift(source_path, function_name):
    """AST-lifts one pure function out of a module that will not import.

    Board trap 21 and `tools/varprobe''s method: `util.py' imports `document',
    which wants half of Qt, but `next_file' itself is `os.path' and integers.
    The function is compiled from UPSTREAM'S OWN SOURCE, so a change to it
    changes the fixture.
    """
    import ast

    with open(source_path, encoding='utf-8') as stream:
        tree = ast.parse(stream.read())

    for node in tree.body:
        if isinstance(node, ast.FunctionDef) and node.name == function_name:
            namespace = {'os': os}
            exec(compile(ast.Module([node], []), source_path, 'exec'), namespace)
            return namespace[function_name]

    raise SystemExit(f'{source_path}: no function {function_name}')


def install_shim(frescobaldi_path):
    """Installs the PyQt6/Frescobaldi stand-ins and puts Frescobaldi on the path.

    Returns the recorder types the caller drives the dialogs with.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    sys.path.insert(0, os.path.join(os.path.dirname(here), 'scorewizprobe'))
    import qtshim

    #python-ly is not needed here; the shim only wants a path it can prepend.
    qtshim.install(frescobaldi_path, frescobaldi_path)

    def _module(name, **members):
        module = types.ModuleType(name)
        for key, value in members.items():
            setattr(module, key, value)
        sys.modules[name] = module
        return module

    #--- the layout calls `toly_dialog' makes that the shim has no answer for.
    qtshim.QLayout.setRowStretch = lambda self, row, stretch: None
    qtshim.QLayout.setColumnStretch = lambda self, column, stretch: None

    #--- the window-level calls a QDialog makes on itself.
    qtshim.QWidget.setWindowModality = lambda self, modality: None
    qtshim.QWidget.setWindowTitle = lambda self, title: setattr(
        self, '_window_title', title)
    qtshim.QWidget.windowTitle = lambda self: getattr(self, '_window_title', '')
    qtshim.QWidget.accept = lambda self: None
    qtshim.QWidget.reject = lambda self: None
    qtshim.QWidget.exec = lambda self: False
    qtshim.QObject.objectName = lambda self: getattr(self, '_object_name', '')

    #--- QTabWidget: `toly_dialog' adds two tabs and reads nothing back.
    class _TabWidget(qtshim.QWidget):
        def __init__(self, parent=None, **kwargs):
            self.tabs = []
            super().__init__(parent, **kwargs)

        def addTab(self, widget, title):
            self.tabs.append((widget, title))

    #--- QDialogButtonBox: the OK button's caption is a msgid this port keeps.
    class _Flag:
        """One standard button; Qt combines two of them with `|'."""

        def __init__(self, name):
            self.name = name

        def __or__(self, other):
            return _Flag(f'{self.name}|{other.name}')

        def __hash__(self):
            return hash(self.name)

        def __eq__(self, other):
            return isinstance(other, _Flag) and other.name == self.name

    class _ButtonBox(qtshim.QWidget):
        StandardButton = types.SimpleNamespace(
            Ok=_Flag('Ok'), Cancel=_Flag('Cancel'))

        def __init__(self, *args, **kwargs):
            self.accepted = qtshim._Signal()
            self.rejected = qtshim._Signal()
            self._buttons = {}
            super().__init__(None)

        def button(self, which):
            return self._buttons.setdefault(which, qtshim.QPushButton())

    qtwidgets = sys.modules['PyQt6.QtWidgets']
    qtwidgets.QTabWidget = _TabWidget
    qtwidgets.QDialogButtonBox = _ButtonBox
    qtwidgets.QTextEdit = qtshim.QLabel
    qtwidgets.QFileDialog = qtshim.QWidget
    qtwidgets.QMessageBox = qtshim.QWidget

    #`util.py' asks QtCore for QDir; nothing this probe reaches uses it.
    qtcore = sys.modules['PyQt6.QtCore']
    qtcore.QDir = type('QDir', (), {'tempPath': staticmethod(lambda: '/tmp')})

    #--- QSettings that actually remembers, so load/save can be round-tripped.
    class _Settings:
        store = {}

        def __init__(self, *args):
            self._group = ''

        def beginGroup(self, group):
            self._group = group

        def endGroup(self):
            self._group = ''

        def _key(self, key):
            return f'{self._group}/{key}' if self._group else key

        def value(self, key, default=None, type=None):
            return _Settings.store.get(self._key(key), default)

        def setValue(self, key, value):
            _Settings.store[self._key(key)] = value

    qtcore.QSettings = _Settings
    qtshim.QSettings = _Settings

    #--- job.Job: upstream builds a command line; here the arguments are kept.
    class _Job:
        def __init__(self, command=None, input=None, output=None,
                     directory=None, encoding=None, **kwargs):
            self.command = command
            self.input = input
            self.output = output
            self.directory = directory
            self.encoding = encoding
            self.environment = {}
            self.args = []

        def add_argument(self, argument):
            self.args.append(argument)

        def output_file(self):
            return self._output_file

    job = _module('job', Job=_Job)
    job.__path__ = []  #`file_import' does `import job.dialog'.
    job.dialog = _module('job.dialog', Dialog=qtshim.QWidget)

    #--- lilychooser: ruling FR5.1 kills the chooser; it answers one version.
    class _Info:
        def toolcommand(self, program):
            return ['lilypond-tool', program]

        def versionString(self):
            return '2.27.2'

    class _LilyChooser(qtshim.QWidget):
        def __init__(self, *args, **kwargs):
            self.currentIndexChanged = qtshim._Signal()
            super().__init__(None)

        def lilyPondInfo(self):
            return _Info()

    _module('lilychooser', LilyChooser=_LilyChooser)

    #--- util: the real module imports `document', which wants half of Qt.
    #`next_file' is PURE and is LIFTED out of upstream's own source instead
    #(board trap 21, `tools/varprobe''s method); `tempdir' is a scratch
    #directory the external command wrote into and nothing here reads.
    _module('util',
            next_file=lift(os.path.join(frescobaldi_path, 'util.py'),
                           'next_file'),
            tempdir=lambda: '/tmp/frescobaldi-import')

    #--- the "What's This?" action `ToLyDialog' installs on itself.
    class _Parent(qtshim.QWidget):
        def __init__(self):
            super().__init__(None)
            self.actionCollection = types.SimpleNamespace(
                help_whatsthis=object())

    return _Settings, _Parent


def dialog_for(module_name, parent):
    """Builds one import dialog, with its settings freshly defaulted."""
    module = __import__(f'file_import.{module_name}', fromlist=['Dialog'])
    return module.Dialog(parent)


def record_dialog(module_name, parent, settings_type, combinations):
    """Records one dialog: its checks, its defaults, its texts, its mapping."""
    dialog = dialog_for(module_name, parent)
    dialog.set_input(INPUT_FILE)

    imp_names = [check._object_name for check in dialog.impChecks]
    post_names = [check._object_name for check in dialog.postChecks]

    #`imp_default' is set by the subclass's loadSettings, which has run.
    record = {
        #⚠ EMPTY for midi and abc: only `musicxml.py' calls setWindowTitle.
        #The C# side declares that difference rather than inheriting it.
        'window_title': dialog.windowTitle(),
        'imp_program': dialog._imp_prgm,
        'userguide_page': dialog._userg,
        'import_checks': imp_names,
        'import_defaults': list(dialog.imp_default),
        'import_texts': [check.text() for check in dialog.impChecks],
        'post_checks': post_names,
        'post_defaults': [check.isChecked() for check in dialog.postChecks],
        'post_texts': [check.text() for check in dialog.postChecks],
        'ok_text': dialog.buttons.button(
            type(dialog.buttons).StandardButton.Ok).text(),
        'cases': [],
    }

    if hasattr(dialog, 'langCombo'):
        record['languages'] = [
            dialog.langCombo._display(index)
            for index in range(dialog.langCombo.count())]

    #⚠ THE MAPPING. Every case sets the checkboxes, runs upstream's own
    #`configure_job()' and keeps the arguments it added.
    for case in combinations(dialog):
        for check, value in zip(dialog.impChecks, case['checks']):
            check.setChecked(value)
        if 'language_index' in case:
            dialog.langCombo.setCurrentIndex(case['language_index'])

        dialog._job = None
        dialog.configure_job()
        record['cases'].append({
            'checks': list(case['checks']),
            'language_index': case.get('language_index', -1),
            'arguments': list(dialog._job.args),
        })

    #The post tab: what `get_post_settings' answers for a set of states.
    post_cases = []
    for state in itertools.product([False, True], repeat=len(dialog.postChecks)):
        for check, value in zip(dialog.postChecks, state):
            check.setChecked(value)
        post_cases.append({
            'checks': list(state),
            'settings': list(dialog.get_post_settings()),
        })

    record['post_cases'] = post_cases

    #The settings round trip: save what is on screen, read it back on a fresh
    #dialog, and record both halves.
    settings_type.store = {}
    for index, check in enumerate(dialog.impChecks):
        check.setChecked(not dialog.imp_default[index])
    for check in dialog.postChecks:
        check.setChecked(True)
    if hasattr(dialog, 'langCombo'):
        dialog.langCombo.setCurrentIndex(dialog.langCombo.count() - 1)

    dialog.saveSettings()
    record['saved_settings'] = dict(settings_type.store)

    reloaded = dialog_for(module_name, parent)
    record['reloaded_import_checks'] = [
        check.isChecked() for check in reloaded.impChecks]
    record['reloaded_post_checks'] = [
        check.isChecked() for check in reloaded.postChecks]
    if hasattr(reloaded, 'langCombo'):
        record['reloaded_language_index'] = reloaded.langCombo.currentIndex()

    settings_type.store = {}
    return record


def musicxml_combinations(dialog):
    """All 64 checkbox combinations at the default language, then all
    languages with every box ticked."""
    for state in itertools.product([False, True], repeat=len(dialog.impChecks)):
        yield {'checks': list(state), 'language_index': 0}

    for index in range(dialog.langCombo.count()):
        yield {'checks': [True] * len(dialog.impChecks),
               'language_index': index}


def simple_combinations(dialog):
    for state in itertools.product([False, True], repeat=len(dialog.impChecks)):
        yield {'checks': list(state)}


INPUT_FILE = '/home/user/scores/song.xml'

NEXT_FILE_NAMES = [
    'song.ly', 'song-1.ly', 'song-9.ly', 'song-10.ly', 'song-0.ly',
    'song-x.ly', 'song-.ly', 'song--1.ly', 'a-b-2.ly', 'noextension',
    'noext-3', '/tmp/dir-2/song.ly', '/tmp/dir-2/song-2.ly',
    'song.tar.gz', 'song- 3.ly', 'song-+4.ly', 'song-03.ly',
]

IMPORTABLE_NAMES = [
    'a.xml', 'a.musicxml', 'a.mxl', 'a.midi', 'a.mid', 'a.abc',
    'a.XML', 'a.Mid', 'a.ly', 'a', 'a.xml.gz', '.abc', 'a.abc.txt',
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frescobaldi', required=True)
    parser.add_argument('--out', required=True)
    arguments = parser.parse_args()

    frescobaldi = os.path.join(
        os.path.expanduser(arguments.frescobaldi), 'frescobaldi')
    settings_type, parent_type = install_shim(frescobaldi)

    util = sys.modules['util']
    from file_import import musicxml

    parent = parent_type()

    fixture = {
        'languages': list(musicxml._langlist),
        'dialogs': {
            'musicxml': record_dialog(
                'musicxml', parent, settings_type, musicxml_combinations),
            'midi': record_dialog(
                'midi', parent, settings_type, simple_combinations),
            'abc': record_dialog(
                'abc', parent, settings_type, simple_combinations),
        },
        'next_file': {name: util.next_file(name) for name in NEXT_FILE_NAMES},
        'importable': {
            name: os.path.splitext(name)[1]
            in ['.xml', '.musicxml', '.mxl', '.midi', '.mid', '.abc']
            for name in IMPORTABLE_NAMES
        },
    }

    #`targets' is built in FileImport.__init__, which needs a main window; the
    #dictionary literal is read out of the source instead, so the fixture
    #records upstream's own table rather than one written here.
    fixture['targets'] = read_targets(
        os.path.join(frescobaldi, 'file_import', '__init__.py'))

    os.makedirs(arguments.out, exist_ok=True)
    path = os.path.join(arguments.out, 'file_import.json')
    with open(path, 'w', encoding='utf-8') as stream:
        json.dump(fixture, stream, indent=1, sort_keys=True)
        stream.write('\n')

    print(f'{path}: {os.path.getsize(path)} bytes')


def read_targets(source_path):
    """Reads `self.targets = {...}' out of upstream's own source.

    `FileImport.__init__' needs a main window to run, and the table is a
    literal, so it is lifted the way `tools/varprobe' lifts definitions
    (board trap 21) rather than being copied by hand.
    """
    import ast

    with open(source_path, encoding='utf-8') as stream:
        tree = ast.parse(stream.read())

    for node in ast.walk(tree):
        if (isinstance(node, ast.Assign)
                and isinstance(node.targets[0], ast.Attribute)
                and node.targets[0].attr == 'targets'):
            return {
                key.value: element.elts[0].value
                for key, element in zip(node.value.keys, node.value.values)
            }

    raise SystemExit('could not find FileImport.targets')


if __name__ == '__main__':
    main()
