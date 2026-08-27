#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs Frescobaldi's OWN Score Wizard
builder over a set of scenarios and writes what it produced as fixtures the
Fresco.Brix.Core.Tests parity test replays.

The scorewiz part types are Qt widgets, so `qtshim.py` stands in for PyQt6 and
for the handful of Frescobaldi modules the wizard imports. Everything that
decides what the LilyPond document says -- `scorewiz/build.py`, every
`scorewiz/parts/*.py`, `scorewiz/scoreproperties.py`, `scorewiz/preview.py`,
`lasptyqu.py` and `ly.dom` itself -- is upstream's own code, executed here.

Every scenario names the part classes by their upstream class name and their
settings by the upstream WIDGET ATTRIBUTE name, which is exactly what the C#
port uses as its setting key, so the fixtures are replayable there.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-scorewiz-fixtures.py [out-dir]
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import qtshim  # noqa: E402  - must be imported before anything upstream

PYTHON_LY = os.path.expanduser('~/ClaudeHome/python-ly')
FRESCOBALDI = os.path.expanduser('~/GitHome/frescobaldi/frescobaldi')
DEFAULT_OUT = os.path.join(
    HERE, '..', '..', 'tests', 'Fresco.Brix.Core.Tests', 'fixtures', 'scorewiz')

LILYPOND_VERSION = '2.27.2'

LILYPONDINFO = qtshim.install(PYTHON_LY, FRESCOBALDI)

import ly.dom                                     # noqa: E402
from scorewiz import build, preview, parts        # noqa: E402
from scorewiz import settings as sw_settings      # noqa: E402
from scorewiz import header as sw_header          # noqa: E402


# ---------------------------------------------------------------- the dialog

class HeaderWidgetStub:
    """Stands in for scorewiz.header.HeaderWidget, which is all painting."""

    def __init__(self, values):
        self._values = values

    def headers(self):
        for name, _desc in sw_header.headers():
            text = self._values.get(name, '').strip()
            if text:
                yield name, text


class Page:
    """The lazy tab wrapper the Builder reaches through."""

    def __init__(self, widget):
        self._widget = widget

    def widget(self):
        return self._widget


class PartsPageStub:
    def __init__(self, root):
        self._root = root

    def widget(self):
        return self

    def rootPartItem(self):
        return self._root


class TreeItem:
    """One row of the score tree: a part plus its children."""

    def __init__(self, part_class, settings=None, children=None):
        self.part = part_class()
        layout = qtshim.QVBoxLayout()
        self.part.createWidgets(layout)
        self.part.translateWidgets()
        apply_settings(self.part, settings or {})
        self._children = list(children or ())

    def childCount(self):
        return len(self._children)

    def child(self, index):
        return self._children[index]


class RootItem:
    """The invisible root; it has no `part` attribute, which is how
    build.PartNode tells it from a real row."""

    def __init__(self, children):
        self._children = list(children)

    def childCount(self):
        return len(self._children)

    def child(self, index):
        return self._children[index]


class DialogStub:
    """Enough of ScoreWizardDialog for build.Builder to read itself from."""

    pitchLanguageChanged = qtshim._signal(str)

    def __init__(self):
        self.finished = qtshim._Signal()
        self._pitchLanguage = ''
        self.header = None
        self.parts = None
        self.settings = None

    def pitchLanguage(self):
        return self._pitchLanguage

    def setPitchLanguage(self, language):
        if language != self._pitchLanguage:
            self._pitchLanguage = language
            self.pitchLanguageChanged.emit(language)


def apply_settings(target, settings):
    """Applies a scenario's settings to a widget-carrying object.

    Keys are upstream's widget attribute names, optionally dotted to reach
    through a sub-object (`generalPreferences.metro`).
    """
    for path, value in settings.items():
        owner = target
        parts_ = path.split('.')
        for name in parts_[:-1]:
            owner = getattr(owner, name)
        widget = getattr(owner, parts_[-1])
        set_widget_value(widget, value)


def set_widget_value(widget, value):
    if isinstance(widget, qtshim.QSpinBox):
        widget.setValue(int(value))
    elif isinstance(widget, (qtshim.QCheckBox, qtshim.QRadioButton,
                             qtshim.QGroupBox)):
        widget.setChecked(bool(value))
    elif isinstance(widget, qtshim.QComboBox):
        if isinstance(value, bool) or not isinstance(value, int):
            widget.setCurrentText(str(value))
        else:
            widget.setCurrentIndex(value)
    elif isinstance(widget, qtshim.QLineEdit):
        widget.setText(str(value))
    else:
        raise SystemExit('cannot set {0!r} on {1!r}'.format(value, widget))


# --------------------------------------------------------------- the running

def make_tree(spec):
    """Builds TreeItems from a nested [class-name, settings, children] spec."""
    items = []
    for entry in spec:
        name = entry['part']
        items.append(TreeItem(
            part_class(name),
            entry.get('settings'),
            make_tree(entry.get('children', []))))
    return items


_PART_CLASSES = {}


def part_class(name):
    if not _PART_CLASSES:
        for category in parts.categories:
            for item in category.items:
                _PART_CLASSES[item.__name__] = item
    return _PART_CLASSES[name]


def run(scenario):
    """Runs one scenario through upstream's builder and returns the fixture."""
    dialog = DialogStub()
    qtshim.CURRENT_WINDOW = dialog
    LILYPONDINFO.VERSION = scenario.get('version', LILYPOND_VERSION)

    dialog.header = Page(HeaderWidgetStub(scenario.get('header', {})))
    settings_widget = sw_settings.SettingsWidget(None)
    dialog.settings = Page(settings_widget)
    dialog.parts = PartsPageStub(RootItem(make_tree(scenario.get('parts', []))))

    language = scenario.get('pitchLanguage', '')
    if language:
        dialog.setPitchLanguage(language)
    apply_settings(settings_widget, scenario.get('settings', {}))

    builder = build.Builder(dialog)
    doc = builder.document()
    if not settings_widget.generalPreferences.relpitch.isChecked():
        for node in doc.find(ly.dom.Relative):
            for pitch in node.find(ly.dom.Pitch, 1):
                node.remove(pitch)
    text = builder.text(doc)

    #The preview builds its own document and fills in example music, exactly
    #as ScoreWizardDialog.showPreview does.
    preview_builder = build.Builder(dialog)
    preview_doc = preview_builder.document()
    preview.examplify(preview_doc)
    preview_text = preview_builder.text(preview_doc)

    return {
        'name': scenario['name'],
        'version': scenario.get('version', LILYPOND_VERSION),
        'pitchLanguage': language,
        'header': scenario.get('header', {}),
        'settings': scenario.get('settings', {}),
        'parts': scenario.get('parts', []),
        'text': text,
        'previewText': preview_text,
    }


# ------------------------------------------------------------- the scenarios

def solo_scenarios():
    """One scenario per part type, alone in the score with its defaults.

    This is what covers every `build()` in the parts package; the scenarios
    below it are about the options.
    """
    for category in parts.categories:
        for item in category.items:
            yield {
                'name': 'solo-{0}'.format(item.__name__),
                'parts': [{'part': item.__name__}],
            }


SCENARIOS = [
    {
        'name': 'empty',
        'parts': [],
    },
    {
        'name': 'headers-all',
        'header': {
            'dedication': 'For Anna',
            'title': 'Sonata "Fine"',
            'subtitle': 'in C major',
            'subsubtitle': 'a study',
            'instrument': 'Violin',
            'composer': "O'Carolan",
            'arranger': 'J. Ellis',
            'poet': 'Anonymous',
            'meter': 'Andante',
            'piece': 'I. Allegro',
            'opus': 'Op. 1',
            'copyright': 'Public Domain',
            'tagline': 'Engraved by hand',
        },
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'headers-typographical-quotes-off',
        'header': {'title': 'A "quoted" title', 'subtitle': "it's fine"},
        'settings': {'generalPreferences.typq': False},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'headers-tagline-suppressed',
        'header': {'title': 'No Tagline'},
        'settings': {'generalPreferences.tagl': True},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'general-all-options',
        'settings': {
            'generalPreferences.relpitch': False,
            'generalPreferences.tagl': True,
            'generalPreferences.barnum': True,
            'generalPreferences.neutdir': True,
            'generalPreferences.metro': True,
            'generalPreferences.paper': 4,
            'generalPreferences.paperOrientation': 1,
        },
        'parts': [{'part': 'Violin'}, {'part': 'Cello'}],
    },
    {
        'name': 'paper-rotated',
        'settings': {
            'generalPreferences.paper': 7,
            'generalPreferences.paperOrientation': 2,
        },
        'parts': [{'part': 'Flute'}],
    },
    {
        'name': 'instrument-names-short-first',
        'settings': {
            'instrumentNames.firstSystem': 1,
            'instrumentNames.otherSystems': 0,
        },
        'parts': [{'part': 'Violin'}, {'part': 'Contrabass'}],
    },
    {
        'name': 'instrument-names-off',
        'settings': {'instrumentNames': False},
        'parts': [{'part': 'Violin'}, {'part': 'Viola'}],
    },
    {
        'name': 'instrument-names-none-none',
        'settings': {
            'instrumentNames.firstSystem': 2,
            'instrumentNames.otherSystems': 2,
        },
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'midi-off',
        'settings': {'midiOutput': False},
        'parts': [{'part': 'Piano'}],
    },
    {
        'name': 'midi-separate-score',
        'settings': {'midiOutput.separateScore': True},
        'parts': [{'part': 'Violin'}, {'part': 'Cello'}],
    },
    {
        'name': 'midi-separate-score-single-part',
        'settings': {'midiOutput.separateScore': True},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'midi-separate-with-metronome',
        'settings': {
            'midiOutput.separateScore': True,
            'generalPreferences.metro': True,
        },
        'parts': [{'part': 'Violin'}, {'part': 'Viola'}],
    },
    {
        'name': 'score-properties-full',
        'settings': {
            'scoreProperties.keyNote': 5,
            'scoreProperties.keyMode': 3,
            'scoreProperties.timeSignature': '7/8',
            'scoreProperties.pickup': 3,
            'scoreProperties.metronomeNote': 6,
            'scoreProperties.metronomeValue': '72',
            'scoreProperties.tempo': 'Allegro moderato',
            'generalPreferences.metro': True,
        },
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'score-properties-tempo-text-only',
        'settings': {'scoreProperties.tempo': 'Andante'},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'time-signature-common',
        'settings': {'scoreProperties.timeSignature': 0},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'time-signature-alla-breve',
        'settings': {'scoreProperties.timeSignature': 1},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'time-signature-compound-unsupported',
        'settings': {'scoreProperties.timeSignature': '3+2/8'},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'pitch-language-english',
        'pitchLanguage': 'english',
        'settings': {'scoreProperties.keyNote': 2},
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'pitch-language-italiano',
        'pitchLanguage': 'italiano',
        'settings': {'scoreProperties.keyNote': 12, 'scoreProperties.keyMode': 1},
        'parts': [{'part': 'Cello'}],
    },
    {
        'name': 'pitch-language-deutsch-old-version',
        'pitchLanguage': 'deutsch',
        'version': '2.12.0',
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'old-version-2-14',
        'version': '2.14.0',
        'settings': {
            'scoreProperties.timeSignature': '5/4',
            'generalPreferences.metro': False,
        },
        'parts': [{'part': 'Violin'}],
    },
    {
        'name': 'piano-two-voices-each',
        'parts': [{'part': 'Piano', 'settings': {
            'upperVoices': 2, 'lowerVoices': 3}}],
    },
    {
        'name': 'piano-no-dynamics-staff',
        'parts': [{'part': 'Piano', 'settings': {'dynamicsStaff': False}}],
    },
    {
        'name': 'piano-midi-instrument',
        'parts': [{'part': 'Piano', 'settings': {'midiInstrumentSelection': 2}}],
    },
    {
        'name': 'organ-percussive-pedal',
        'parts': [{'part': 'Organ', 'settings': {
            'midiInstrumentSelection': 1, 'pedalVoices': 2, 'upperVoices': 2}}],
    },
    {
        'name': 'organ-no-pedal',
        'parts': [{'part': 'Organ', 'settings': {'pedalVoices': 0}}],
    },
    {
        'name': 'synth-lead-upper-only',
        'parts': [{'part': 'SynthLead', 'settings': {
            'upperVoices': 1, 'lowerVoices': 0}}],
    },
    {
        'name': 'synth-bass-lower-only',
        'parts': [{'part': 'SynthBass', 'settings': {
            'upperVoices': 0, 'lowerVoices': 1}}],
    },
    {
        'name': 'guitar-tab-only',
        'parts': [{'part': 'Guitar', 'settings': {'staffType': 1}}],
    },
    {
        'name': 'guitar-both-staves',
        'parts': [{'part': 'Guitar', 'settings': {
            'staffType': 2, 'voices': 2, 'tuning': 3}}],
    },
    {
        'name': 'guitar-no-octave-clef',
        'parts': [{'part': 'Guitar', 'settings': {'octaveClef': False}}],
    },
    {
        'name': 'guitar-four-voices-tab',
        'parts': [{'part': 'Guitar', 'settings': {'staffType': 1, 'voices': 4}}],
    },
    {
        'name': 'guitar-three-voices',
        'parts': [{'part': 'Guitar', 'settings': {'staffType': 2, 'voices': 3}}],
    },
    {
        'name': 'guitar-custom-tuning',
        'parts': [{'part': 'Guitar', 'settings': {
            'staffType': 1, 'tuning': 10, 'customTuning': "e, a d g b e'"}}],
    },
    {
        'name': 'guitar-default-tuning',
        'parts': [{'part': 'Guitar', 'settings': {'staffType': 1, 'tuning': 0}}],
    },
    {
        'name': 'banjo-four-strings',
        'parts': [{'part': 'Banjo', 'settings': {
            'staffType': 1, 'tuning': 2, 'fourStrings': True}}],
    },
    {
        'name': 'banjo-five-strings',
        'parts': [{'part': 'Banjo', 'settings': {'staffType': 1, 'tuning': 2}}],
    },
    {
        'name': 'electric-guitar-midi',
        'parts': [{'part': 'ElectricGuitar', 'settings': {
            'staffType': 2, 'midiInstrumentSelection': 4}}],
    },
    {
        'name': 'harp-voices',
        'parts': [{'part': 'Harp', 'settings': {
            'upperVoices': 2, 'lowerVoices': 2}}],
    },
    {
        'name': 'marimba-single-staff',
        'parts': [{'part': 'Marimba', 'settings': {'lowerVoices': 0}}],
    },
    {
        'name': 'carillon-no-dynamics',
        'parts': [{'part': 'Carillon', 'settings': {'dynamicsStaff': False}}],
    },
    {
        'name': 'drums-styles',
        'parts': [{'part': 'Drums', 'settings': {
            'voices': 3, 'drumStyle': 2, 'drumStems': True}}],
    },
    {
        'name': 'drums-percussion-style',
        'parts': [{'part': 'Drums', 'settings': {'drumStyle': 4}}],
    },
    {
        'name': 'figured-bass-extenders',
        'parts': [{'part': 'BassFigures', 'settings': {'extenderLines': True}}],
    },
    {
        'name': 'chords-french-frets',
        'parts': [{'part': 'Chords', 'settings': {
            'chordStyle': 4, 'guitarFrets': True}}],
    },
    {
        'name': 'vocal-solo-stanzas-ambitus',
        'parts': [{'part': 'SopranoVoice', 'settings': {
            'stanzas': 3, 'ambitus': True}}],
    },
    {
        'name': 'vocal-solo-one-stanza',
        'parts': [{'part': 'TenorVoice', 'settings': {'stanzas': 1}}],
    },
    {
        'name': 'leadsheet-accompaniment',
        'parts': [{'part': 'LeadSheet', 'settings': {
            'accomp': True, 'stanzas': 2}}],
    },
    {
        'name': 'leadsheet-accompaniment-ambitus',
        'parts': [{'part': 'LeadSheet', 'settings': {
            'accomp': True, 'ambitus': True, 'stanzas': 2}}],
    },
    {
        'name': 'leadsheet-accompaniment-ambitus-one-stanza',
        'parts': [{'part': 'LeadSheet', 'settings': {
            'accomp': True, 'ambitus': True}}],
    },
    {
        'name': 'leadsheet-no-chords',
        'parts': [{'part': 'LeadSheet', 'settings': {'chords': False}}],
    },
    {
        'name': 'choir-satb-piano-reduction',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'SA-TB', 'pianoReduction': True}}],
    },
    {
        'name': 'choir-satb-separate-staves',
        'parts': [{'part': 'Choir', 'settings': {'voicing': 'S-A-T-B'}}],
    },
    {
        'name': 'choir-every-voice-different-lyrics',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'S-A-T-B', 'lyrics': 2, 'stanzas': 2}}],
    },
    {
        'name': 'choir-every-voice-same-lyrics',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'SS-A-TT-B', 'lyrics': 1}}],
    },
    {
        'name': 'choir-distribute-stanzas',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'S-A-T-B', 'lyrics': 3, 'stanzas': 5}}],
    },
    {
        'name': 'choir-male-piano-reduction',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'TT-B', 'pianoReduction': True}}],
    },
    {
        'name': 'choir-female-piano-reduction',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'S-S-A', 'pianoReduction': True}}],
    },
    {
        'name': 'choir-ambitus-four-voices',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'SSAA-TTBB', 'ambitus': True}}],
    },
    {
        'name': 'choir-rehearsal-midi',
        'settings': {'generalPreferences.metro': True},
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'SA-TB', 'rehearsalMidi': True}}],
    },
    {
        'name': 'choir-rehearsal-midi-old-version',
        'version': '2.16.0',
        'parts': [{'part': 'Choir', 'settings': {
            'voicing': 'S-A', 'rehearsalMidi': True}}],
    },
    {
        'name': 'choir-two-choirs',
        'parts': [
            {'part': 'Choir', 'settings': {'voicing': 'SA-TB'}},
            {'part': 'Choir', 'settings': {'voicing': 'SA-TB'}},
        ],
    },
    {
        'name': 'choir-empty-voicing',
        'parts': [{'part': 'Choir', 'settings': {'voicing': '---'}}],
    },
    {
        'name': 'choir-messy-voicing',
        'parts': [{'part': 'Choir', 'settings': {'voicing': '-sa--tb-'}}],
    },
    {
        'name': 'staffgroup-brace',
        'parts': [{'part': 'StaffGroup', 'settings': {'systemStart': 0},
                   'children': [{'part': 'Violin'}, {'part': 'Viola'}]}],
    },
    {
        'name': 'staffgroup-brace-no-barlines',
        'parts': [{'part': 'StaffGroup', 'settings': {
            'systemStart': 0, 'connectBarLines': False},
            'children': [{'part': 'Violin'}, {'part': 'Cello'}]}],
    },
    {
        'name': 'staffgroup-bracket-no-barlines',
        'parts': [{'part': 'StaffGroup', 'settings': {'connectBarLines': False},
                   'children': [{'part': 'Violin'}, {'part': 'Cello'}]}],
    },
    {
        'name': 'staffgroup-square',
        'parts': [{'part': 'StaffGroup', 'settings': {'systemStart': 2},
                   'children': [{'part': 'Flute'}, {'part': 'Oboe'}]}],
    },
    {
        'name': 'staffgroup-nested',
        'parts': [{'part': 'StaffGroup', 'children': [
            {'part': 'Violin'},
            {'part': 'StaffGroup', 'settings': {'systemStart': 2},
             'children': [{'part': 'Viola'}, {'part': 'Cello'}]},
        ]}],
    },
    {
        'name': 'score-with-own-properties',
        'parts': [{'part': 'Score', 'settings': {
            'piece': 'I. Prelude', 'opus': 'BWV 846', 'scoreProps': True,
            'timeSignature': '3/4', 'keyNote': 3, 'keyMode': 1},
            'children': [{'part': 'Violin'}]}],
    },
    {
        'name': 'score-piece-opus-only',
        'parts': [{'part': 'Score', 'settings': {
            'piece': 'Andante', 'opus': 'Op. 3'},
            'children': [{'part': 'Flute'}]}],
    },
    {
        'name': 'two-scores-shared-parts',
        'parts': [
            {'part': 'Violin'},
            {'part': 'Score', 'settings': {'piece': 'One'}},
            {'part': 'Score', 'settings': {'piece': 'Two'}},
        ],
    },
    {
        'name': 'two-scores-own-parts',
        'parts': [
            {'part': 'Score', 'settings': {'piece': 'One'},
             'children': [{'part': 'Violin'}]},
            {'part': 'Score', 'settings': {'piece': 'Two'},
             'children': [{'part': 'Cello'}]},
        ],
    },
    {
        'name': 'book-with-bookparts',
        'parts': [{'part': 'Book', 'settings': {
            'bookOutput': 'parts', 'bookOutputSuffix': True}, 'children': [
                {'part': 'BookPart', 'children': [
                    {'part': 'Score', 'children': [{'part': 'Violin'}]}]},
                {'part': 'BookPart', 'children': [
                    {'part': 'Score', 'children': [{'part': 'Cello'}]}]},
            ]}],
    },
    {
        'name': 'book-output-filename',
        'parts': [{'part': 'Book', 'settings': {
            'bookOutput': 'my "score"', 'bookOutputFileName': True},
            'children': [{'part': 'Violin'}]}],
    },
    {
        'name': 'many-parts-same-type',
        'parts': [
            {'part': 'Violin'}, {'part': 'Violin'}, {'part': 'Viola'},
            {'part': 'Cello'}, {'part': 'BassoContinuo'},
        ],
    },
    {
        'name': 'orchestra',
        'header': {'title': 'Symphony', 'composer': 'Anonymous'},
        'settings': {
            'generalPreferences.metro': True,
            'scoreProperties.timeSignature': '3/4',
            'scoreProperties.keyNote': 10,
            'scoreProperties.keyMode': 1,
        },
        'parts': [
            {'part': 'StaffGroup', 'settings': {'systemStart': 2}, 'children': [
                {'part': 'Flute'}, {'part': 'Oboe'}, {'part': 'Clarinet'},
                {'part': 'Bassoon'}]},
            {'part': 'StaffGroup', 'settings': {'systemStart': 2}, 'children': [
                {'part': 'HornF'}, {'part': 'TrumpetBb'}, {'part': 'Trombone'}]},
            {'part': 'Timpani'},
            {'part': 'StaffGroup', 'children': [
                {'part': 'Violin'}, {'part': 'Violin'}, {'part': 'Viola'},
                {'part': 'Cello'}, {'part': 'Contrabass'}]},
        ],
    },
    {
        'name': 'lead-sheet-song',
        'header': {'title': 'A Song', 'poet': 'Anon', 'composer': 'Anon'},
        'settings': {'scoreProperties.timeSignature': '4/4'},
        'parts': [{'part': 'LeadSheet', 'settings': {
            'stanzas': 2, 'chordStyle': 1}}],
    },
    {
        'name': 'structure-and-parts',
        'parts': [{'part': 'Structure'}, {'part': 'Violin'}, {'part': 'Chords'}],
    },
]


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)

    scenarios = list(solo_scenarios()) + SCENARIOS
    results = []
    for scenario in scenarios:
        try:
            results.append(run(scenario))
        except Exception as error:               # noqa: BLE001 - report and stop
            raise SystemExit('scenario {0!r} failed: {1!r}'.format(
                scenario['name'], error))

    path = os.path.join(out_dir, 'scorewiz.json')
    with open(path, 'w', encoding='utf-8') as handle:
        json.dump(results, handle, indent=1, ensure_ascii=False, sort_keys=True)
        handle.write('\n')

    #The part catalogue itself: the categories, their part types in order, and
    #each type's title and abbreviation, so the C# registry is checked too.
    catalogue = []
    for category in parts.categories:
        catalogue.append({
            'title': category.title(),
            'items': [{
                'name': item.__name__,
                'title': item.title(),
                'short': item.short() or '',
            } for item in category.items],
        })
    path = os.path.join(out_dir, 'parts.json')
    with open(path, 'w', encoding='utf-8') as handle:
        json.dump(catalogue, handle, indent=1, ensure_ascii=False)
        handle.write('\n')

    print('wrote {0} scenarios and {1} categories to {2}'.format(
        len(results), len(catalogue), out_dir))


if __name__ == '__main__':
    main()
