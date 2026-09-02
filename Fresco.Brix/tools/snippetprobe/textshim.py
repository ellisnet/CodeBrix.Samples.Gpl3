#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): a stand-in for the small part of
PyQt6's rich-text model that Frescobaldi's twenty-two PYTHON snippets use, so
that upstream's OWN snippet bodies can be executed here and used as the parity
oracle for the native commands FD10 replaces them with.

Trap 46's precedent: code that IS a Qt client can still be the oracle, provided
the shim only has to be true where that code looks.  These snippet bodies look
at QTextDocument, QTextBlock and QTextCursor and at nothing else of Qt; the
model below implements exactly those three, to Qt's documented semantics:

  * a document is a list of BLOCKS separated by one paragraph separator each;
  * block.length() is len(block.text()) + 1, for every block including the
    last, so the document's character count is len(plainText) + 1 and the
    largest addressable cursor position is len(plainText);
  * BlockUnderCursor selects from the END of the previous block (that is, it
    swallows the separator in front of the block) to the end of this one, and
    from the block's own start when there is no previous block.

Everything the snippets import beyond Qt is either upstream's own module,
loaded from the reference checkout unchanged (`cursortools`), or python-ly
itself under the name Frescobaldi gives its Qt-backed subclass
(`lydocument` -> `ly.document`), which is the same interface over the same
tokenizer.  Nothing here re-implements a snippet.

PyQt6 is NOT installed on this machine and must not be (standing rule 11).
"""
import importlib.util
import os
import sys
import types


FRESCOBALDI = os.environ.get(
    'FRESCOBALDI_ROOT', os.path.expanduser('~/GitHome/frescobaldi'))
PYTHON_LY = os.environ.get(
    'PYTHON_LY_ROOT', os.path.expanduser('~/ClaudeHome/python-ly'))


class _Enum:
    """A namespace whose every attribute is a unique named sentinel."""

    def __init__(self, path):
        self._path = path
        self._members = {}

    def __getattr__(self, name):
        if name.startswith('_'):
            raise AttributeError(name)
        if name not in self._members:
            self._members[name] = _Enum('{0}.{1}'.format(self._path, name))
        return self._members[name]

    def __hash__(self):
        return hash(self._path)

    def __eq__(self, other):
        return isinstance(other, _Enum) and other._path == self._path

    def __repr__(self):
        return self._path


class QTextBlock:
    """One paragraph of a document.

    A block is identified by its NUMBER in the document; an out-of-range
    number is the invalid block Qt hands back at either end.
    """

    def __init__(self, document=None, number=-1):
        self._document = document
        self._number = number

    def isValid(self):
        return (self._document is not None
                and 0 <= self._number < len(self._document._lines))

    def document(self):
        return self._document

    def blockNumber(self):
        return self._number

    def text(self):
        return self._document._lines[self._number] if self.isValid() else ''

    def position(self):
        """The document position of the block's first character."""
        if not self.isValid():
            return -1
        return sum(len(line) + 1 for line in self._document._lines[:self._number])

    def length(self):
        """len(text) + 1 — the paragraph separator counts, in every block."""
        return len(self.text()) + 1 if self.isValid() else 0

    def next(self):
        return QTextBlock(self._document, self._number + 1)

    def previous(self):
        return QTextBlock(self._document, self._number - 1)

    def _key(self):
        return self._number if self.isValid() else -1

    def __eq__(self, other):
        return (isinstance(other, QTextBlock)
                and other._document is self._document
                and other._key() == self._key())

    def __ne__(self, other):
        return not self.__eq__(other)

    def __lt__(self, other):
        return self._key() < other._key()

    def __le__(self, other):
        return self._key() <= other._key()

    def __gt__(self, other):
        return self._key() > other._key()

    def __ge__(self, other):
        return self._key() >= other._key()

    def __hash__(self):
        return hash((id(self._document), self._number))


class QTextDocument:
    """A document held as a list of lines."""

    def __init__(self, text=''):
        self.setPlainText(text)

    def setPlainText(self, text):
        self._lines = text.split('\n')

    def toPlainText(self):
        return '\n'.join(self._lines)

    def characterCount(self):
        return len(self.toPlainText()) + 1

    def blockCount(self):
        return len(self._lines)

    def firstBlock(self):
        return QTextBlock(self, 0)

    def lastBlock(self):
        return QTextBlock(self, len(self._lines) - 1)

    def findBlock(self, position):
        position = max(0, min(position, len(self.toPlainText())))
        offset = 0
        for number, line in enumerate(self._lines):
            if position <= offset + len(line):
                return QTextBlock(self, number)
            offset += len(line) + 1
        return self.lastBlock()

    def _replace(self, start, end, text):
        plain = self.toPlainText()
        self.setPlainText(plain[:start] + text + plain[end:])


class QTextCursor:
    """A position, an anchor, and the operations the snippets ask of them."""

    MoveMode = _Enum('QTextCursor.MoveMode')
    MoveOperation = _Enum('QTextCursor.MoveOperation')
    SelectionType = _Enum('QTextCursor.SelectionType')

    def __init__(self, other=None):
        if isinstance(other, QTextCursor):
            self._document = other._document
            self._position = other._position
            self._anchor = other._anchor
        elif isinstance(other, QTextBlock):
            self._document = other.document()
            self._position = other.position()
            self._anchor = self._position
        elif isinstance(other, QTextDocument):
            self._document = other
            self._position = 0
            self._anchor = 0
        else:
            raise TypeError('a cursor needs a document')

    #-- state ------------------------------------------------------------

    def document(self):
        return self._document

    def position(self):
        return self._position

    def anchor(self):
        return self._anchor

    def selectionStart(self):
        return min(self._position, self._anchor)

    def selectionEnd(self):
        return max(self._position, self._anchor)

    def hasSelection(self):
        return self._position != self._anchor

    def block(self):
        return self._document.findBlock(self._position)

    def atBlockStart(self):
        return self._position == self.block().position()

    def atBlockEnd(self):
        block = self.block()
        return self._position == block.position() + len(block.text())

    def clearSelection(self):
        self._anchor = self._position

    def selection(self):
        return _Selection(
            self._document.toPlainText()[self.selectionStart():self.selectionEnd()])

    #-- movement ---------------------------------------------------------

    def setPosition(self, position, mode=None):
        limit = len(self._document.toPlainText())
        self._position = max(0, min(position, limit))
        if mode != QTextCursor.MoveMode.KeepAnchor:
            self._anchor = self._position

    def movePosition(self, operation, mode=None, n=1):
        for _ in range(n):
            if not self._moveOnce(operation, mode):
                return False
        return True

    def _moveOnce(self, operation, mode):
        block = self.block()
        op = QTextCursor.MoveOperation
        if operation == op.StartOfBlock:
            self.setPosition(block.position(), mode)
            return True
        if operation == op.EndOfBlock:
            self.setPosition(block.position() + len(block.text()), mode)
            return True
        if operation == op.NextBlock:
            following = block.next()
            if not following.isValid():
                return False
            self.setPosition(following.position(), mode)
            return True
        if operation == op.PreviousBlock:
            preceding = block.previous()
            if not preceding.isValid():
                return False
            self.setPosition(preceding.position(), mode)
            return True
        if operation == op.Left:
            if self._position == 0:
                return False
            self.setPosition(self._position - 1, mode)
            return True
        if operation == op.Right:
            if self._position >= len(self._document.toPlainText()):
                return False
            self.setPosition(self._position + 1, mode)
            return True
        if operation == op.End:
            self.setPosition(len(self._document.toPlainText()), mode)
            return True
        if operation == op.Start:
            self.setPosition(0, mode)
            return True
        raise NotImplementedError(repr(operation))

    def select(self, selectionType):
        if selectionType != QTextCursor.SelectionType.BlockUnderCursor:
            raise NotImplementedError(repr(selectionType))
        block = self.block()
        preceding = block.previous()
        start = (preceding.position() + len(preceding.text())
                 if preceding.isValid() else block.position())
        self._anchor = start
        self._position = block.position() + len(block.text())

    #-- editing ----------------------------------------------------------

    def insertText(self, text):
        start, end = self.selectionStart(), self.selectionEnd()
        self._document._replace(start, end, text)
        self._position = self._anchor = start + len(text)

    def removeSelectedText(self):
        self.insertText('')

    #-- edit blocks are a no-op here (there is no undo stack) ------------

    def beginEditBlock(self):
        pass

    def joinPreviousEditBlock(self):
        pass

    def endEditBlock(self):
        pass


class _Selection:
    def __init__(self, text):
        self._text = text

    def toPlainText(self):
        return self._text


class _BlockUserData:
    pass


def install():
    """Puts the shim, upstream's own modules and python-ly on sys.modules."""
    if 'PyQt6' in sys.modules:
        return

    qtgui = types.ModuleType('PyQt6.QtGui')
    qtgui.QTextBlock = QTextBlock
    qtgui.QTextCursor = QTextCursor
    qtgui.QTextDocument = QTextDocument
    qtgui.QTextBlockUserData = _BlockUserData

    qtcore = types.ModuleType('PyQt6.QtCore')
    qtcore.Qt = _Enum('Qt')
    qtcore.QUrl = object
    qtcore.QSettings = object

    qtwidgets = types.ModuleType('PyQt6.QtWidgets')
    qtwidgets.QMessageBox = object
    qtwidgets.QMenu = object

    pyqt = types.ModuleType('PyQt6')
    pyqt.QtGui = qtgui
    pyqt.QtCore = qtcore
    pyqt.QtWidgets = qtwidgets

    sys.modules['PyQt6'] = pyqt
    sys.modules['PyQt6.QtGui'] = qtgui
    sys.modules['PyQt6.QtCore'] = qtcore
    sys.modules['PyQt6.QtWidgets'] = qtwidgets

    if PYTHON_LY not in sys.path:
        sys.path.insert(0, PYTHON_LY)

    #Upstream's own cursortools, byte for byte, running on the shim.
    sys.modules['cursortools'] = _load('cursortools')

    #Frescobaldi's `lydocument` is python-ly's document API over a
    #QTextDocument; the snippets use only the parts that ARE python-ly's, so
    #python-ly answers for it directly.
    import ly.document
    lydocument = types.ModuleType('lydocument')
    lydocument.Cursor = ly.document.Cursor
    lydocument.Document = ly.document.Document
    lydocument.Runner = ly.document.Runner
    lydocument.Source = ly.document.Source

    def cursor(qcursor, select_all=False):
        document = ly.document.Document(qcursor.document().toPlainText())
        if not select_all or qcursor.hasSelection():
            return ly.document.Cursor(
                document, qcursor.selectionStart(), qcursor.selectionEnd())
        return ly.document.Cursor(document, 0, None)

    lydocument.cursor = cursor
    sys.modules['lydocument'] = lydocument


def _load(name):
    """Imports one module out of the Frescobaldi checkout by path."""
    path = os.path.join(FRESCOBALDI, 'frescobaldi', name + '.py')
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def _load_by_path(name, *parts):
    """Imports one module out of the Frescobaldi checkout by path."""
    path = os.path.join(FRESCOBALDI, 'frescobaldi', *parts)
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def builtin_snippets():
    """Upstream's builtin.py, imported unchanged (it needs no Qt at all)."""
    import builtins
    if not hasattr(builtins, '_'):
        builtins._ = lambda *args: args[-1]
    return _load_by_path(
        'snippet_builtin', 'snippet', 'builtin.py').builtin_snippets


def snippet_parser():
    """Upstream's snippet/snippets.py, for its own variable parsing."""
    return _load_by_path('snippet_snippets', 'snippet', 'snippets.py')
