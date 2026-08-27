#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): a stand-in for PyQt6 and for the
Frescobaldi modules the Score Wizard's part types import, so that upstream's
OWN scorewiz code can be executed here and used as the parity oracle.

Frescobaldi's part types ARE Qt widgets: `createWidgets` builds spin boxes and
combo boxes, and `build` reads them back. PyQt6 is not installed on this
machine and must not be, so trap 21's answer (lift the pure definitions out by
AST) does not reach this code -- there is no pure half to lift.

What this module does instead is install a shim: enough of QtCore/QtGui/
QtWidgets, and of `app`, `icons`, `symbols`, `listmodel`, `i18n` and friends,
that `scorewiz.build` and every `scorewiz.parts.*` module import, construct
their widgets, and run their `build()` unchanged. The ly.dom trees that come
out are therefore genuinely upstream's, not a reading of upstream.

The shim is deliberately dumb: widgets are property bags, layouts are lists,
signals are callback lists. Nothing here paints, and nothing here is a model of
Qt -- it only has to be true where the scorewiz code looks.
"""
import sys
import types


class _Enum:
    """A namespace whose every attribute is a unique sentinel.

    Qt enum access in the ported code is only ever compared for identity or
    used as a dictionary key (`Qt.ItemDataRole.DisplayRole`), so a sentinel per
    name is all that is needed -- and nested access keeps working however deep
    the code reaches.
    """

    def __init__(self, path='Qt'):
        self._path = path
        self._members = {}

    def __getattr__(self, name):
        if name.startswith('_'):
            raise AttributeError(name)
        if name not in self._members:
            self._members[name] = _Enum('{0}.{1}'.format(self._path, name))
        return self._members[name]

    def __or__(self, other):
        return self

    def __ror__(self, other):
        return self

    def __hash__(self):
        return hash(self._path)

    def __repr__(self):
        return self._path


class _Signal:
    """A signal: a list of callbacks that `emit` walks."""

    def __init__(self):
        self._slots = []

    def connect(self, slot):
        self._slots.append(slot)

    def disconnect(self, slot=None):
        if slot is None:
            self._slots = []
        elif slot in self._slots:
            self._slots.remove(slot)

    def emit(self, *args):
        for slot in list(self._slots):
            slot(*args)


def _signal(*_types):
    """pyqtSignal() stand-in: a descriptor handing out one _Signal per object."""

    class _Descriptor:
        def __init__(self):
            self._name = '_signal_{0}'.format(id(self))

        def __get__(self, instance, owner=None):
            if instance is None:
                return self
            if not hasattr(instance, self._name):
                setattr(instance, self._name, _Signal())
            return getattr(instance, self._name)

    return _Descriptor()


#The window every widget answers with; the probe sets it before building.
CURRENT_WINDOW = None


class QObject:
    """The root of the shim's object tree."""

    def __init__(self, parent=None, **kwargs):
        self._parent = parent
        self._enabled = True
        self._tooltip = ''
        self.destroyed = _Signal()
        self._apply(kwargs)

    def _apply(self, kwargs):
        """Applies Qt's keyword-argument-as-property-setter convention."""
        for name, value in kwargs.items():
            setter = 'set' + name[0].upper() + name[1:]
            if hasattr(self, setter):
                getattr(self, setter)(value)
            else:
                setattr(self, '_' + name, value)

    def parent(self):
        return self._parent

    def window(self):
        return CURRENT_WINDOW

    def setParent(self, parent):
        self._parent = parent

    def setObjectName(self, name):
        self._object_name = name

    def deleteLater(self):
        pass

    def addAction(self, *args):
        pass

    def setToolTip(self, text):
        self._tooltip = text

    def toolTip(self):
        return self._tooltip

    def setEnabled(self, enabled):
        self._enabled = bool(enabled)

    def isEnabled(self):
        return self._enabled

    def setWhatsThis(self, text):
        pass

    def setFocus(self, *args):
        pass

    def setFocusPolicy(self, policy):
        pass

    def setMinimumSize(self, *args):
        pass

    def setContentsMargins(self, *args):
        pass

    def setSizePolicy(self, *args):
        pass

    def setIconSize(self, size):
        pass

    def setIcon(self, icon):
        pass

    def show(self):
        pass

    def hide(self):
        pass

    def setVisible(self, visible):
        pass

    def palette(self):
        return _Palette()


class _Palette:
    def color(self, role):
        return _Color()

    def setColor(self, role, color):
        pass


class _Color:
    def name(self):
        return '#000000'


class QWidget(QObject):
    def __init__(self, parent=None, **kwargs):
        self._layout = None
        super().__init__(parent, **kwargs)

    def setLayout(self, layout):
        self._layout = layout

    def layout(self):
        return self._layout


class QLabel(QWidget):
    def __init__(self, text='', parent=None, **kwargs):
        self._text = text
        super().__init__(parent, **kwargs)

    def setText(self, text):
        self._text = text

    def text(self):
        return self._text

    def setBuddy(self, widget):
        self._buddy = widget

    def setWordWrap(self, wrap):
        pass

    def setFixedWidth(self, width):
        pass

    def minimumSizeHint(self):
        return QSize(0, 0)

    def setOpenLinks(self, value):
        pass

    def setOpenExternalLinks(self, value):
        pass


class QAbstractButton(QWidget):
    def __init__(self, parent=None, **kwargs):
        self._text = ''
        self._checked = False
        self.clicked = _Signal()
        self.toggled = _Signal()
        super().__init__(parent, **kwargs)

    def setText(self, text):
        self._text = text

    def text(self):
        return self._text

    def setChecked(self, checked):
        self._checked = bool(checked)
        self.toggled.emit(self._checked)

    def isChecked(self):
        return self._checked

    def setCheckable(self, value):
        pass


class QCheckBox(QAbstractButton):
    pass


class QRadioButton(QAbstractButton):
    pass


class QPushButton(QAbstractButton):
    pass


class QToolButton(QAbstractButton):
    pass


class QGroupBox(QWidget):
    def __init__(self, parent=None, **kwargs):
        self._title = ''
        self._checkable = False
        self._checked = True
        super().__init__(parent, **kwargs)

    def setTitle(self, title):
        self._title = title

    def title(self):
        return self._title

    def setCheckable(self, value):
        self._checkable = bool(value)

    def isCheckable(self):
        return self._checkable

    def setChecked(self, checked):
        self._checked = bool(checked)

    def isChecked(self):
        return self._checked


class QSpinBox(QWidget):
    def __init__(self, parent=None, **kwargs):
        self._minimum = 0
        self._maximum = 99
        self._value = 0
        self.valueChanged = _Signal()
        super().__init__(parent, **kwargs)

    def setMinimum(self, value):
        self._minimum = value
        if self._value < value:
            self.setValue(value)

    def minimum(self):
        return self._minimum

    def setMaximum(self, value):
        self._maximum = value
        if self._value > value:
            self.setValue(value)

    def maximum(self):
        return self._maximum

    def setValue(self, value):
        value = max(self._minimum, min(self._maximum, value))
        changed = value != self._value
        self._value = value
        if changed:
            self.valueChanged.emit(value)

    def value(self):
        return self._value


class _Completer:
    def setCaseSensitivity(self, sensitivity):
        pass


class QLineEdit(QWidget):
    def __init__(self, parent=None, **kwargs):
        self._text = ''
        self._completer = _Completer()
        self.textChanged = _Signal()
        self.textEdited = _Signal()
        super().__init__(parent, **kwargs)

    def setText(self, text):
        self._text = text or ''
        self.textChanged.emit(self._text)

    def text(self):
        return self._text

    def clear(self):
        self.setText('')

    def setPlaceholderText(self, text):
        pass

    def setClearButtonEnabled(self, value):
        pass

    def setCompleter(self, completer):
        self._completer = completer

    def completer(self):
        return self._completer

    def setValidator(self, validator):
        pass


class _ModelStub:
    """The listmodel.ListModel stand-in a combo box reads its items from."""

    def __init__(self, data=None, display=None):
        self._data = list(data or ())
        self._display = display
        self.dataChanged = _Signal()

    def display_of(self, index):
        try:
            item = self._data[index]
        except IndexError:
            return ''
        if self._display is None:
            return str(item)
        return str(self._display(item))

    def count(self):
        return len(self._data)

    def update(self):
        pass

    def createIndex(self, *args):
        return None


class QComboBox(QWidget):
    def __init__(self, parent=None, **kwargs):
        self._model = _ModelStub()
        self._index = -1
        self._edit_text = None
        self._editable = False
        self._item_texts = {}
        self.activated = _Signal()
        self.currentIndexChanged = _Signal()
        self.currentTextChanged = _Signal()
        super().__init__(parent, **kwargs)

    def setEditable(self, value):
        self._editable = bool(value)

    def setModel(self, model):
        self._model = model
        self._item_texts = {}
        if self._index < 0 and model.count():
            self.setCurrentIndex(0)

    def model(self):
        return self._model

    def addItem(self, text, data=None):
        self._model._data.append(text)
        if self._index < 0:
            self.setCurrentIndex(0)

    def addItems(self, texts):
        for text in texts:
            self.addItem(text)

    def count(self):
        return self._model.count()

    def setCurrentIndex(self, index):
        changed = index != self._index
        self._index = index
        self._edit_text = None
        if changed:
            self.currentIndexChanged.emit(index)

    def currentIndex(self):
        return self._index

    def setCurrentText(self, text):
        """Qt picks the matching item if there is one, else sets the edit text."""
        for index in range(self._model.count()):
            if self._display(index) == text:
                self.setCurrentIndex(index)
                return
        self.setEditText(text)

    def setEditText(self, text):
        self._edit_text = text

    def currentText(self):
        if self._edit_text is not None:
            return self._edit_text
        return self._display(self._index)

    def _display(self, index):
        if index in self._item_texts:
            return self._item_texts[index]
        return self._model.display_of(index)

    def setItemText(self, index, text):
        self._item_texts[index] = text

    def setItemData(self, index, data, role=None):
        pass

    def setCompleter(self, completer):
        pass

    def completer(self):
        return _Completer()

    def setValidator(self, validator):
        pass

    def view(self):
        return QWidget()


class _LayoutItem:
    def __init__(self, widget=None, layout=None):
        self._widget = widget
        self._layout = layout

    def widget(self):
        return self._widget

    def layout(self):
        return self._layout


class QLayout(QObject):
    def __init__(self, parent=None, **kwargs):
        self._items = []
        super().__init__(parent, **kwargs)

    def addWidget(self, widget, *args, **kwargs):
        self._items.append(_LayoutItem(widget=widget))

    def addLayout(self, layout, *args, **kwargs):
        self._items.append(_LayoutItem(layout=layout))

    def addStretch(self, *args):
        self._items.append(_LayoutItem())

    def addItem(self, item):
        self._items.append(item)

    def count(self):
        return len(self._items)

    def itemAt(self, index):
        try:
            return self._items[index]
        except IndexError:
            return None

    def setSpacing(self, spacing):
        pass

    def setVerticalSpacing(self, spacing):
        pass

    def setHorizontalSpacing(self, spacing):
        pass

    def setColumnMinimumWidth(self, column, width):
        pass

    def setColumnStretch(self, column, stretch):
        pass


class QVBoxLayout(QLayout):
    pass


class QHBoxLayout(QLayout):
    pass


class QGridLayout(QLayout):
    pass


class QStackedLayout(QLayout):
    pass


class QSize:
    def __init__(self, width=0, height=0):
        self._width = width
        self._height = height

    def width(self):
        return self._width

    def height(self):
        return self._height


class QSettings:
    """Always answers the caller's default -- a fresh install's settings."""

    def __init__(self, *args):
        self._group = ''

    def beginGroup(self, group):
        self._group = group

    def endGroup(self):
        self._group = ''

    def value(self, key, default=None, type=None):
        return default

    def setValue(self, key, value):
        pass


class QUrl:
    def __init__(self, url=''):
        self._url = url

    @staticmethod
    def fromLocalFile(path):
        return QUrl(path)

    def toString(self):
        return self._url


class QRegularExpression:
    PatternOption = _Enum('QRegularExpression.PatternOption')

    def __init__(self, pattern=''):
        self._pattern = pattern

    def setPatternOptions(self, options):
        pass


class QRegularExpressionValidator:
    def __init__(self, *args):
        pass


class QIntValidator:
    def __init__(self, *args):
        pass


def _module(name, **members):
    """Registers a stub module under `name` and returns it."""
    module = types.ModuleType(name)
    for key, value in members.items():
        setattr(module, key, value)
    sys.modules[name] = module
    return module


def install(python_ly_path, frescobaldi_path):
    """Installs the shim, then puts python-ly and Frescobaldi on the path.

    Call this before importing anything out of `scorewiz`.
    """
    #_() is a builtin in Frescobaldi, installed by i18n. The context form takes
    #the message as its SECOND argument; the plural form takes a count third.
    def translate(*args):
        if len(args) == 1:
            return args[0]
        if len(args) == 2:
            return args[1] if isinstance(args[1], str) else args[0]
        if len(args) == 3 and isinstance(args[2], int):
            return args[0] if args[2] == 1 else args[1]
        if len(args) == 4:
            return args[1] if args[3] == 1 else args[2]
        return args[0]

    import builtins
    builtins._ = translate

    qtcore = _module(
        'PyQt6.QtCore',
        Qt=_Enum('Qt'),
        QObject=QObject,
        QSize=QSize,
        QSettings=QSettings,
        QUrl=QUrl,
        QRegularExpression=QRegularExpression,
        QAbstractListModel=QObject,
        QAbstractItemModel=QObject,
        QModelIndex=object,
        pyqtSignal=_signal,
        QTimer=QObject,
    )
    qtgui = _module(
        'PyQt6.QtGui',
        QAction=QObject,
        QKeySequence=lambda *a: None,
        QIntValidator=QIntValidator,
        QRegularExpressionValidator=QRegularExpressionValidator,
        QPalette=_Enum('QPalette'),
        QColor=_Color,
        QIcon=object,
        QTextCursor=object,
    )
    qtwidgets = _module(
        'PyQt6.QtWidgets',
        QWidget=QWidget,
        QLabel=QLabel,
        QCheckBox=QCheckBox,
        QRadioButton=QRadioButton,
        QPushButton=QPushButton,
        QToolButton=QToolButton,
        QGroupBox=QGroupBox,
        QSpinBox=QSpinBox,
        QLineEdit=QLineEdit,
        QComboBox=QComboBox,
        QCompleter=_Completer,
        QVBoxLayout=QVBoxLayout,
        QHBoxLayout=QHBoxLayout,
        QGridLayout=QGridLayout,
        QStackedLayout=QStackedLayout,
        QStackedWidget=QWidget,
        QSplitter=QWidget,
        QTabWidget=QWidget,
        QTreeView=QWidget,
        QTextBrowser=QLabel,
        QDialog=QWidget,
        QDialogButtonBox=QWidget,
        QApplication=QObject,
        QButtonGroup=QObject,
    )
    pyqt6 = _module('PyQt6')
    pyqt6.QtCore = qtcore
    pyqt6.QtGui = qtgui
    pyqt6.QtWidgets = qtwidgets

    sys.path.insert(0, python_ly_path)
    sys.path.insert(0, frescobaldi_path)

    #The Frescobaldi modules the scorewiz code imports but that have nothing to
    #say about the document it builds.
    _module('app', translateUI=lambda obj, *a: obj.translateUI(),
            settingsChanged=_Signal(), languageChanged=_Signal(),
            aboutToQuit=_Signal(), caption=lambda text: text,
            appendUrl=lambda *a: None, openUrl=lambda *a: None,
            activeWindow=lambda: CURRENT_WINDOW, is_git_controlled=lambda: False)
    _module('icons', get=lambda name: None)
    _module('symbols', icon=lambda name: None)
    _module('completionmodel', complete=lambda *a, **k: None)
    _module('qutil', getAccelerator=lambda text: None,
            addAccelerators=lambda *a: None, saveDialogSize=lambda *a: None,
            mixcolor=lambda *a: _Color())
    _module('userguide', addButton=lambda *a: None)
    _module('plugin', MainWindowPlugin=object)
    _module('textformats', formatData=lambda name: None)
    _module('listmodel',
            display=lambda item: item,
            translate=lambda item: item(),
            display_index=lambda index: (lambda item: item[index]),
            translate_index=lambda index: (lambda item: item[index]()),
            ListModel=lambda data, parent=None, display=lambda i: i, edit=None,
            tooltip=None, icon=None: _ModelStub(data, display))
    _module('language_names', languageName=lambda code, current=None: code)

    i18n_setup = _module('i18n.setup', current=lambda: 'en')
    i18n = _module('i18n', available=lambda: [], translator=lambda lang: translate,
                   setup=i18n_setup)
    i18n.setup = i18n_setup

    lilypondinfo = _module('lilypondinfo')

    class _Info:
        def __init__(self, version='2.27.2'):
            self._version = version

        def versionString(self):
            return self._version

    lilypondinfo.preferred = lambda: _Info(lilypondinfo.VERSION)
    lilypondinfo.VERSION = '2.27.2'

    treewidget = _module('widgets.treewidget', TreeWidget=QWidget,
                         TreeWidgetItem=QObject)
    tempobutton = _module('widgets.tempobutton', TempoButton=type(
        'TempoButton', (QWidget,), {'tempo': _Signal()}))
    widgets = _module('widgets', treewidget=treewidget, tempobutton=tempobutton,
                      Separator=QWidget)
    widgets.treewidget = treewidget
    widgets.tempobutton = tempobutton

    return lilypondinfo
