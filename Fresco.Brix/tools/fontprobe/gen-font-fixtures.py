#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs Frescobaldi's OWN
`fonts/fontcommand.py` and `fonts/musicfonts.py` and records what they produce,
so the Document Fonts port is verified against upstream rather than against
itself (board rule "never record a fixture from the port's own output").

Two fixtures come out of one run:

  fixtures/fonts/fontcommand.json   every scenario's four generated commands --
                                    the traditional ("lily") and openLilyLib
                                    ("oll") approaches, each in the filtered
                                    form the Font Command tab shows and the
                                    "full" form the preview engraves.
  fixtures/fonts/musicfonts.json    what upstream's MusicFontFamily makes of a
                                    list of file names: which are music fonts
                                    at all, and each family's type/size
                                    classification, brace flag, missing sizes
                                    and completeness.

`fonts/fontcommand.py` IS a Qt widget (a QScrollArea full of check boxes), so
board trap 46 applies rather than trap 21: the reusable
`tools/scorewizprobe/qtshim.py` module stands in for PyQt6 and the real
upstream code runs unchanged.  qtshim is REUSED, not copied; the handful of
widget classes this module needs and scorewiz did not are added to the already
installed `PyQt6.QtWidgets` here, so qtshim itself is untouched.

PyQt6 is NOT installed on this machine and must not be (standing rule 11).

Usage:
    python3 tools/fontprobe/gen-font-fixtures.py [output-directory]

The default output directory is tests/Fresco.Brix.Core.Tests/fixtures/fonts.
"""
import json
import os
import sys
import types


HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
FRESCOBALDI = os.environ.get(
    'FRESCOBALDI_ROOT', os.path.expanduser('~/GitHome/frescobaldi/frescobaldi'))
PYTHON_LY = os.environ.get(
    'PYTHON_LY_ROOT', os.path.expanduser('~/ClaudeHome/python-ly'))

sys.path.insert(0, os.path.join(REPO, 'tools', 'scorewizprobe'))
import qtshim  # noqa: E402


# --------------------------------------------------------------------------
# The widgets fontcommand.py uses that the score wizard never did.
# --------------------------------------------------------------------------

class QScrollArea(qtshim.QWidget):
    Shape = qtshim._Enum('QScrollArea.Shape')

    def setWidgetResizable(self, value):
        pass

    def setWidget(self, widget):
        self._widget = widget

    def setFrameShape(self, shape):
        pass


class QTextEdit(qtshim.QWidget):
    def __init__(self, parent=None, **kwargs):
        self._plain = ''
        super().__init__(parent, **kwargs)

    def setReadOnly(self, value):
        pass

    def setPlainText(self, text):
        self._plain = text

    def toPlainText(self):
        return self._plain

    def document(self):
        return None


class QTabWidget(qtshim.QWidget):
    """Enough of a tab widget for `approach_tab`: pages, a current index and
    the `currentChanged` signal the command generator listens to."""

    def __init__(self, parent=None, **kwargs):
        self._pages = []
        self._titles = []
        self._index = 0
        self.currentChanged = qtshim._Signal()
        super().__init__(parent, **kwargs)

    def addTab(self, widget, title):
        self._pages.append(widget)
        self._titles.append(title)
        return len(self._pages) - 1

    def setTabText(self, index, text):
        self._titles[index] = text

    def setTabToolTip(self, index, text):
        pass

    def setCurrentIndex(self, index):
        self._index = int(index)
        self.currentChanged.emit(self._index)

    def currentIndex(self):
        return self._index


class QButtonGroup(qtshim.QObject):
    """The stylesheet radio group: ids, the checked id, and `buttonClicked`."""

    def __init__(self, parent=None, **kwargs):
        self._buttons = []
        self._ids = {}
        self.buttonClicked = qtshim._Signal()
        super().__init__(parent, **kwargs)

    def addButton(self, button, id=None):
        self._buttons.append(button)
        if id is not None:
            self._ids[id] = button

    def setId(self, button, id):
        self._ids[id] = button

    def checkedId(self):
        for identifier, button in sorted(self._ids.items()):
            if button.isChecked():
                return identifier
        return -1

    def buttons(self):
        return list(self._buttons)


class QLineEdit(qtshim.QLineEdit):
    """qtshim's line edit plus `editingFinished`, which the command generator
    connects to (its signal map is keyed by widget base class)."""

    def __init__(self, parent=None, **kwargs):
        self.editingFinished = qtshim._Signal()
        super().__init__(parent, **kwargs)


class QStandardItem(qtshim.QObject):
    def __init__(self, text='', *args, **kwargs):
        self._text = text
        self._rows = []
        super().__init__(None)

    def setText(self, text):
        self._text = text

    def text(self):
        return self._text

    def setCheckState(self, state):
        self._state = state

    def setFont(self, font):
        pass

    def appendRow(self, row):
        self._rows.append(row)


class QStandardItemModel(qtshim.QObject):
    def __init__(self, parent=None, **kwargs):
        self._root = QStandardItem()
        super().__init__(parent, **kwargs)

    def clear(self):
        self._root = QStandardItem()

    def setColumnCount(self, count):
        pass

    def setHeaderData(self, *args):
        pass

    def invisibleRootItem(self):
        return self._root


class QFontDatabase:
    @staticmethod
    def styles(family):
        return []

    @staticmethod
    def addApplicationFont(path):
        return -1


def install():
    """Installs qtshim, then the extra widgets the fonts package needs."""
    qtshim.install(PYTHON_LY, FRESCOBALDI)

    widgets = sys.modules['PyQt6.QtWidgets']
    widgets.QAbstractButton = qtshim.QAbstractButton
    widgets.QScrollArea = QScrollArea
    widgets.QTextEdit = QTextEdit
    widgets.QTabWidget = QTabWidget
    widgets.QButtonGroup = QButtonGroup
    widgets.QAbstractItemView = qtshim.QWidget
    widgets.QFileDialog = qtshim.QWidget
    widgets.QMessageBox = qtshim.QWidget
    widgets.QMenu = qtshim.QWidget
    widgets.QLineEdit = QLineEdit
    sys.modules['PyQt6'].QtWidgets = widgets

    gui = sys.modules['PyQt6.QtGui']
    gui.QStandardItem = QStandardItem
    gui.QStandardItemModel = QStandardItemModel
    gui.QFontDatabase = QFontDatabase
    gui.QFont = qtshim.QObject
    sys.modules['PyQt6'].QtGui = gui

    core = sys.modules['PyQt6.QtCore']
    core.QSortFilterProxyModel = QStandardItemModel
    sys.modules['PyQt6'].QtCore = core

    # fontcommand.py highlights the command it displays; nothing about the text
    # it generated depends on that.
    module = types.ModuleType('highlighter')
    module.highlight = lambda *args, **kwargs: None
    sys.modules['highlighter'] = module

    # musicfonts.py imports `fonts` (the package) and `app`; the package's own
    # __init__ imports `plugin`, `actioncollection` and friends, all of which
    # qtshim already stubs or which are harmless here.
    module = types.ModuleType('job')
    module.Job = object
    sys.modules['job'] = module
    module = types.ModuleType('signals')
    module.Signal = qtshim._signal
    sys.modules['signals'] = module


class FakeDialog:
    """Stands in for `fonts.dialog.FontsDialog` -- the `window()` the command
    widget reaches through.  Upstream's dialog answers `selected_font(family)`,
    carries a `finished` signal the widget saves its settings on, and has
    `show_sample()` called after every regeneration."""

    def __init__(self, fonts):
        self._fonts = dict(fonts)
        self.finished = qtshim._Signal()
        self.samples = 0

    def selected_font(self, family):
        return self._fonts[family]

    def select_font(self, family, name):
        self._fonts[family] = name

    def show_sample(self):
        self.samples += 1


# --------------------------------------------------------------------------
# Scenarios
# --------------------------------------------------------------------------

# The dialog's own defaults (fonts/dialog.py `_default_fonts`).
DEFAULT_FONTS = {
    'music': 'emmentaler',
    'brace': 'emmentaler',
    'roman': 'TeXGyre Schola',
    'sans': 'TeXGyre Heros',
    'typewriter': 'TeXGyre Cursor',
}

# A second selection, so a scenario proves the names travel rather than that a
# default happens to match.  "LilyJAZZ" is a real notation font with its own
# brace font; the text families are three of the port's own vendored faces.
CHOSEN_FONTS = {
    'music': 'lilyjazz',
    'brace': 'lilyjazz',
    'roman': 'C059',
    'sans': 'Nimbus Sans',
    'typewriter': 'Nimbus Mono PS',
}

# A music font WITHOUT a brace font of its own: the music tab then leaves the
# brace at emmentaler, which is a state the command has to be able to express.
MIXED_FONTS = {
    'music': 'Beethoven',
    'brace': 'emmentaler',
    'roman': 'TeX Gyre Schola',
    'sans': 'TeX Gyre Heros',
    'typewriter': 'TeX Gyre Cursor',
}


def scenarios():
    """Yields (name, fonts, options) for every recorded scenario."""

    def case(name, fonts=None, **options):
        settings = {
            'set-music': True,
            'set-roman': False,
            'set-sans': False,
            'set-typewriter': False,
            'set-paper-block': True,
            'approach-index': 0,
            'load-oll': True,
            'load-package': True,
            'font-extensions': False,
            'style-type': 0,
            'font-stylesheet': '',
        }
        settings.update(options)
        return name, dict(fonts or DEFAULT_FONTS), settings

    yield case('defaults')
    yield case('chosen-fonts', CHOSEN_FONTS)
    yield case('mixed-brace', MIXED_FONTS)

    # One family at a time, then every combination of the three text families.
    for roman in (False, True):
        for sans in (False, True):
            for typewriter in (False, True):
                name = 'families-{0}{1}{2}'.format(
                    'r' if roman else '-',
                    's' if sans else '-',
                    't' if typewriter else '-')
                yield case(
                    name, CHOSEN_FONTS,
                    **{'set-roman': roman, 'set-sans': sans,
                       'set-typewriter': typewriter})

    # The music font on its own, and off.
    yield case('no-music', CHOSEN_FONTS, **{'set-music': False})
    yield case('no-music-all-text', CHOSEN_FONTS,
               **{'set-music': False, 'set-roman': True, 'set-sans': True,
                  'set-typewriter': True})
    yield case('nothing-at-all', CHOSEN_FONTS, **{'set-music': False})

    # The \paper wrapper.
    yield case('no-paper-block', CHOSEN_FONTS, **{'set-paper-block': False})
    yield case('no-paper-block-all', CHOSEN_FONTS,
               **{'set-paper-block': False, 'set-roman': True,
                  'set-sans': True, 'set-typewriter': True})

    # openLilyLib: every flag, and the three stylesheet choices.
    for oll in (False, True):
        for package in (False, True):
            name = 'oll-{0}{1}'.format(
                'o' if oll else '-', 'p' if package else '-')
            yield case(name, CHOSEN_FONTS,
                       **{'approach-index': 1, 'load-oll': oll,
                          'load-package': package})

    yield case('oll-extensions', CHOSEN_FONTS,
               **{'approach-index': 1, 'font-extensions': True})
    yield case('oll-style-none', CHOSEN_FONTS,
               **{'approach-index': 1, 'style-type': 1})
    yield case('oll-style-custom', CHOSEN_FONTS,
               **{'approach-index': 1, 'style-type': 2,
                  'font-stylesheet': 'my-style.ily'})
    yield case('oll-style-custom-empty', CHOSEN_FONTS,
               **{'approach-index': 1, 'style-type': 2})
    yield case('oll-text-families', CHOSEN_FONTS,
               **{'approach-index': 1, 'set-roman': True, 'set-sans': True,
                  'set-typewriter': True})
    yield case('oll-everything', CHOSEN_FONTS,
               **{'approach-index': 1, 'set-roman': True, 'set-sans': True,
                  'set-typewriter': True, 'font-extensions': True,
                  'style-type': 2, 'font-stylesheet': 'jazz.ily'})
    yield case('oll-defaults', DEFAULT_FONTS, **{'approach-index': 1})
    yield case('oll-mixed-brace', MIXED_FONTS, **{'approach-index': 1})


def build_widget(fontcommand, fonts, options):
    """Constructs upstream's FontCommandWidget and puts it in the scenario's
    state.  loadSettings() reads a QSettings that always answers the default,
    so the state is applied to the widgets afterwards -- which is exactly what
    a user clicking the boxes does."""
    dialog = FakeDialog(fonts)
    qtshim.CURRENT_WINDOW = dialog

    widget = fontcommand.FontCommandWidget(dialog)
    widget.cb_music.setChecked(options['set-music'])
    widget.cb_roman.setChecked(options['set-roman'])
    widget.cb_sans.setChecked(options['set-sans'])
    widget.cb_typewriter.setChecked(options['set-typewriter'])
    widget.cb_paper_block.setChecked(options['set-paper-block'])
    widget.cb_oll.setChecked(options['load-oll'])
    widget.cb_loadpackage.setChecked(options['load-package'])
    widget.cb_extensions.setChecked(options['font-extensions'])
    for index, button in enumerate(widget.style_buttons):
        button.setChecked(index == options['style-type'])
    widget.le_stylesheet.setText(options['font-stylesheet'])
    widget.approach_tab.setCurrentIndex(options['approach-index'])
    widget.invalidate_command()
    return widget


def fontcommand_fixture():
    """Records every scenario's generated commands."""
    import fonts.fontcommand as fontcommand

    recorded = []
    for name, fonts_map, options in scenarios():
        widget = build_widget(fontcommand, fonts_map, options)
        recorded.append({
            'name': name,
            'fonts': fonts_map,
            'options': options,
            'approach': widget.approach,
            'lilyCommand': widget.command('lily'),
            'lilyFullCommand': widget.full_cmd('lily'),
            'ollCommand': widget.command('oll'),
            'ollFullCommand': widget.full_cmd('oll'),
            'displayedCommand': widget.command_edit.toPlainText(),
        })
    return recorded


# --------------------------------------------------------------------------
# Music font classification
# --------------------------------------------------------------------------

MUSIC_FONT_FILES = [
    # A complete Emmentaler in all three types.
    ['emmentaler-{0}.otf'.format(size) for size in
     ('11', '13', '14', '16', '18', '20', '23', '26')]
    + ['emmentaler-brace.otf']
    + ['emmentaler-{0}.svg'.format(size) for size in
       ('11', '13', '14', '16', '18', '20', '23', '26')]
    + ['emmentaler-brace.svg']
    + ['emmentaler-{0}.woff'.format(size) for size in
       ('11', '13', '14', '16', '18', '20', '23', '26')]
    + ['emmentaler-brace.woff'],

    # OTF only, complete, with a brace.
    ['lilyjazz-{0}.otf'.format(size) for size in
     ('11', '13', '14', '16', '18', '20', '23', '26')]
    + ['lilyjazz-brace.otf'],

    # OTF only, missing sizes, no brace.
    ['beethoven-11.otf', 'beethoven-20.otf', 'beethoven-26.otf'],

    # Nothing that is a music font at all.
    ['README.md', 'emmentaler.otf', 'emmentaler-9.otf', 'emmentaler-20.ttf',
     'emmentaler-brace.pfb', 'font-20.otf.bak'],

    # A brace-only family, and a family whose name itself has a hyphen and a
    # digit in it -- the regular expression has to take the LAST hyphen group.
    ['haydn-brace.otf', 'improviso-20.otf', 'my-font-2-16.otf',
     'my-font-2-brace.svg'],
]


def musicfonts_fixture():
    """Records what upstream's MusicFontFamily makes of each file list."""
    import fonts.musicfonts as musicfonts

    recorded = []
    for index, files in enumerate(MUSIC_FONT_FILES):
        parsed = []
        families = {}
        for name in sorted(files):
            match = musicfonts.MusicFontFamily.parse_filename(name)
            entry = {'file': name, 'isMusicFont': bool(match)}
            if match:
                entry['family'] = match['family']
                entry['size'] = match['size']
                entry['type'] = match['type']
                family = families.setdefault(
                    match['family'], musicfonts.MusicFontFamily())
                family.family = match['family']
                family.add(match['type'], match['size'], name)
            parsed.append(entry)

        summary = []
        for family_name in sorted(families):
            family = families[family_name]
            summary.append({
                'family': family_name,
                'complete': family.is_complete(),
                'types': {
                    kind: {
                        'sizes': family.sizes(kind),
                        'missingSizes': family.missing_sizes(kind),
                        'hasBrace': family.has_brace(kind),
                        'complete': family.is_complete(kind),
                    }
                    for kind in ('otf', 'svg', 'woff')
                },
            })

        recorded.append({
            'name': 'set-{0}'.format(index),
            'files': parsed,
            'families': summary,
            'sizesList': list(musicfonts.MusicFontFamily.sizes_list),
        })
    return recorded


def main():
    output = (
        sys.argv[1] if len(sys.argv) > 1
        else os.path.join(
            REPO, 'tests', 'Fresco.Brix.Core.Tests', 'fixtures', 'fonts'))
    os.makedirs(output, exist_ok=True)

    install()

    commands = fontcommand_fixture()
    path = os.path.join(output, 'fontcommand.json')
    with open(path, 'w', encoding='utf-8') as handle:
        json.dump(commands, handle, indent=2, ensure_ascii=False)
        handle.write('\n')
    print('{0} scenarios -> {1}'.format(len(commands), path))

    fonts_fixture = musicfonts_fixture()
    path = os.path.join(output, 'musicfonts.json')
    with open(path, 'w', encoding='utf-8') as handle:
        json.dump(fonts_fixture, handle, indent=2, ensure_ascii=False)
        handle.write('\n')
    print('{0} file sets -> {1}'.format(len(fonts_fixture), path))


if __name__ == '__main__':
    main()
