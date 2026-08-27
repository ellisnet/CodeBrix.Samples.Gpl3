#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): records the MusicXML python-ly writes
for a set of probe .ly files, as the oracle the Fresco.Brix.Ly MusicXML parity
tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Each fixture is <name>.musicxml beside a copy of <name>.ly, plus
one <name>.musicxml.json recording what the writer was ASKED (its options) and
what it reported back.

TWO FIELDS ARE VOLATILE AND ARE NORMALISED HERE, both inside <encoding>:

  <encoding-date>  python-ly stamps TODAY into it, so a fixture recorded on one
                   day would fail on the next. Upstream's own test suite
                   rewrites it to today's date before comparing
                   (tests/test_xml.py, encoding_date_element_re); the fixture
                   holds the placeholder ENCODING-DATE instead and the C# test
                   substitutes the same way.
  <software>       python-ly writes its own package version, and Frescobaldi
                   OVERWRITES that with its own name and version before saving
                   (file_export/__init__.py). It is the caller's string, not a
                   fact about the music, so the fixture holds SOFTWARE.

Nothing else is touched: what is recorded is byte for byte what python-ly
produced.

KNOWN FIXES (ruling FR14). The oracle answers "what python-ly produces with its
demonstrable defects fixed", not "what python-ly ships with", because that is
what the port is required to implement. The list is the SAME one
tools/musicprobe applies and for the same reason -- the MusicXML writer walks
the very ly.music tree that tool records, so an unfixed `\\repeat` would put a
differently-shaped tree in front of it. Each entry is a ONE-LINE source patch
applied to the reference IN MEMORY at generation time (the checkout stays
read-only, standing rule 3), every fixture records the applied list in its own
JSON, and MusicXmlParityTests asserts it. A fix whose `old` line is no longer
present, or is present more than once, is a hard failure here.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-musicxml-fixtures.py <out-dir> <ly-file>...
"""
import io
import json
import os
import re
import sys
import types

PYTHON_LY = os.path.expanduser('~/ClaudeHome/python-ly')
sys.path.insert(0, PYTHON_LY)

KNOWN_FIXES = [
    {
        # ly/music/read.py:646, in Reader.handle_repeat. The guard tests the
        # BOUND METHOD `item.specifier`, which is always truthy, where the line
        # above it and the branch body both use the FIELD `item._specifier`. The
        # branch is therefore dead and a QUOTED repeat specifier is never read:
        # `\repeat "unfold" 5 { ... }` makes the repeat take the String as its
        # one child and END there, spilling the count and the whole body into
        # the surrounding music and leaving the repeat's length at 0. Comparing
        # the field -- plainly what upstream meant -- gives specifier "unfold",
        # count 5, the music as the repeat's one child, and length 5 x body.
        'module': 'ly.music.read',
        'path': 'ly/music/read.py',
        'old': 'elif not item.specifier and isinstance(t, lex.StringStart):',
        'new': 'elif not item._specifier and isinstance(t, lex.StringStart):',
        'why': 'handle_repeat guards on the bound method item.specifier instead '
               'of the field item._specifier, so a quoted repeat specifier is '
               'never read and the repeat ends at the string (FR14)',
    },
    {
        # ly/musicxml/lymus2musxml.py:166-188, ParseSource.Assignment. The value
        # is classified by a four-arm if/elif chain -- Markup, String, Scheme,
        # UserCommand -- with NO else, and then `val` is used unconditionally
        # two lines below. Any assignment whose value is none of those four
        # raises UnboundLocalError and the whole export dies. `left-margin =
        # 20\mm` is one: the value is a Command, it is legal LilyPond, and it
        # is in LilyPond's own regression corpus (bookparts.ly). What upstream
        # MEANS to do with a value it does not understand is written into the
        # arm right above the crash -- "Don't know what to do with this: return"
        # -- so turning that arm's condition into the else it should have been
        # both fixes the crash and keeps upstream's own answer.
        'module': 'ly.musicxml.lymus2musxml',
        'path': 'ly/musicxml/lymus2musxml.py',
        'old': '        elif isinstance(a.value(), ly.music.items.UserCommand):',
        'new': '        else:',
        'why': 'Assignment classifies the value with a four-arm chain that has '
               'no else and then reads `val` unconditionally, so any other '
               'value type -- a Command such as 20\\mm -- raises '
               'UnboundLocalError and kills the export (FR14)',
    },
    {
        # ly/musicxml/ly2xml_mediator.py:1104, in calc_trem_dur. It calls
        # `xml_objs.durval2type(...)` -- and xml_objs has no such member. The
        # function is defined in ly2xml_mediator ITSELF, four lines of module
        # scope away, and every other caller in the file reaches it unqualified.
        # So a `\repeat tremolo N { ... }` with a repeat count raises
        # AttributeError, which ParseSource then swallows into "Warning: Note
        # not implemented!" -- the tremolo simply vanishes from the output and
        # nothing says why. With the qualifier dropped, `\repeat tremolo 8
        # { c32 e }` comes out as two quarter notes carrying three tremolo
        # beams, which is what the music says.
        'module': 'ly.musicxml.ly2xml_mediator',
        'path': 'ly/musicxml/ly2xml_mediator.py',
        'old': '    new_type = xml_objs.durval2type(trem_length)',
        'new': '    new_type = durval2type(trem_length)',
        'why': 'calc_trem_dur qualifies durval2type with xml_objs, which does '
               'not define it, so every counted tremolo raises AttributeError '
               'and is silently dropped from the export (FR14)',
    },
    {
        # ly/musicxml/ly2xml_mediator.py:240, in Mediator.check_voices. The line
        # above it reads `self.sections[-1]`; this one reads `self.section[-1]`,
        # and there is no such field. Every global section -- which is what
        # `\new Devnull` makes -- therefore raises AttributeError the moment it
        # ends, ParseSource swallows it as "Warning: End not implemented!", and
        # the rest of check_voices (the merge that pairs the two most recent
        # sections) never runs at all. The plural is plainly what was meant.
        'module': 'ly.musicxml.ly2xml_mediator',
        'path': 'ly/musicxml/ly2xml_mediator.py',
        'old': '            self.score.glob_section.merge_voice(self.section[-1])',
        'new': '            self.score.glob_section.merge_voice(self.sections[-1])',
        'why': 'check_voices reads self.section where the field is '
               'self.sections, so a global section raises AttributeError on '
               'close and the voice merge below it is skipped (FR14)',
    },
    {
        # ly/musicxml/xml_objs.py:883, class BarBackup. Bar.is_skip walks a
        # bar's object list calling has_attr() on EVERY item -- and BarBackup is
        # the one bar object that does not define it, so a list holding a backup
        # raises AttributeError. It is reachable the moment a global section is
        # merged into a part that already has one (which is what `\new Devnull`
        # arranges, once the check_voices fix above lets that merge happen at
        # all), and it kills the whole export. BarMus and BarAttr both answer the
        # question; the backup simply forgot to, and the answer is plainly False
        # -- a backup carries no attributes.
        'module': 'ly.musicxml.xml_objs',
        'path': 'ly/musicxml/xml_objs.py',
        'old': '    """ Object that stores duration for backup """',
        'new': '    """ Object that stores duration for backup """\n'
               '    def has_attr(self):\n'
               '        return False\n',
        'why': 'BarBackup does not define has_attr, which Bar.is_skip calls on '
               'every object in a bar, so a bar holding a backup raises '
               'AttributeError and kills the export (FR14)',
    },
    # ------------------------------------------------------------------
    # RULING FR15 -- CONFORMANCE TO THE PUBLISHED MusicXML SCHEMA.
    #
    # The seven entries below are a different KIND of fix from the five above.
    # Those are defects in what upstream MEANT to do. These are places where
    # upstream writes something the MusicXML schema FORBIDS -- and Fresco.Brix
    # does not write a file that fails to conform, at all times, no exceptions.
    #
    # They are applied to the ORACLE as well, and deliberately, because that is
    # what makes the parity tests worth having: with them in place the port and
    # the reference still match BYTE FOR BYTE, which is the evidence that the
    # conformance work moved information around and lost none of it.
    #
    # The schema is neither ours nor upstream's. MusicXML is developed by the
    # W3C Music Notation Community Group; the normative sources are at
    # https://github.com/w3c/musicxml and the reference documentation at
    # https://www.w3.org/2021/06/musicxml40/. A copy is vendored at
    # tests/libs/Fresco.Brix.Ly.Tests/schema/, and MusicXmlSchemaTests validates
    # every document in this corpus against it.
    # ------------------------------------------------------------------
    {
        # <identification>'s children are an xs:SEQUENCE:
        #   identification (creator*, rights*, encoding?, source?, relation*,
        #                   miscellaneous?)
        # <encoding> is built in the constructor, so appending a creator or a
        # rights element AFTER it is invalid -- which means every score with a
        # composer that python-ly has ever written is rejected by the very
        # schema its own test suite validates against. Its 17 test documents
        # have no creators, so it never noticed. The same edit adds the method
        # the next entry needs.
        'module': 'ly.musicxml.create_musicxml',
        'path': 'ly/musicxml/create_musicxml.py',
        'old': '        info_node = etree.SubElement(self.score_info, tag, attr)\n'
               '        info_node.text = info',
        'new': '        info_node = etree.Element(tag, attr)\n'
               '        info_node.text = info\n'
               '        index = get_tag_index(self.score_info, "encoding")\n'
               '        if index == -1:\n'
               '            self.score_info.append(info_node)\n'
               '        else:\n'
               '            self.score_info.insert(index, info_node)\n'
               '\n'
               '    def add_miscellaneous_field(self, name, info):\n'
               '        """Metadata MusicXML has no element of its own for."""\n'
               '        misc = self.score_info.find("miscellaneous")\n'
               '        if misc is None:\n'
               '            misc = etree.SubElement(self.score_info, "miscellaneous")\n'
               '        field = etree.SubElement(misc, "miscellaneous-field", {"name": name})\n'
               '        field.text = info',
        'why': 'identification children are an xs:sequence and creators/rights '
               'come before encoding, so they must be inserted, not appended '
               '(FR15)',
    },
    {
        # IterateXmlObjs -- the LilyPond header variables MusicXML has no
        # element for (subtitle, opus, piece, dedication, ...) are written as
        # bare children of <identification>, which no version of the schema
        # allows. The specification's own answer, in the schema's words: "If a
        # program has other metadata not yet supported in the MusicXML format,
        # it can go in the miscellaneous element."
        'module': 'ly.musicxml.xml_objs',
        'path': 'ly/musicxml/xml_objs.py',
        'old': '            self.musxml.create_score_info(itag, score.info[itag])',
        'new': '            self.musxml.add_miscellaneous_field(itag, score.info[itag])',
        'why': 'header variables the schema does not model belong in '
               '<miscellaneous-field>, not as bare children of <identification> '
               '(FR15)',
    },
    {
        # The version the document declares. 3.0 is MakeMusic's last release;
        # 4.0 is what the W3C Music Notation Community Group publishes and what
        # this corpus is validated against. The content is a subset satisfying
        # both, so the move costs nothing.
        'module': 'ly.musicxml.create_musicxml',
        'path': 'ly/musicxml/create_musicxml.py',
        'old': 'etree.Element("score-partwise", version="3.0")',
        'new': 'etree.Element("score-partwise", version="4.0")',
        'why': 'the document declares the version it is validated against (FR15)',
    },
    {
        # The DOCTYPE names MusicXML 2.0 on a root element declaring 3.0: a
        # document announcing two different versions of itself. The public
        # identifier used here is the one the specification's own catalog.xml
        # lists.
        'module': 'ly.musicxml.create_musicxml',
        'path': 'ly/musicxml/create_musicxml.py',
        'old': 'PUBLIC "-//Recordare//DTD MusicXML 2.0 Partwise//EN"',
        'new': 'PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN"',
        'why': 'the DOCTYPE must name the same version as the root element (FR15)',
    },
    {
        # new_unpitched_note -- the note's sequence reaches <voice> through the
        # editorial-voice group, BEFORE <type>:
        #   ... duration, tie?, instrument*, [footnote?, level?, voice?], type?, dot* ...
        # new_note has it the right way round; this one is reversed, which makes
        # every drum note in a document invalid.
        'module': 'ly.musicxml.create_musicxml',
        'path': 'ly/musicxml/create_musicxml.py',
        'old': '        self.add_duration_type(durtype)\n'
               '        self.add_voice(voice)',
        'new': '        self.add_voice(voice)\n'
               '        self.add_duration_type(durtype)',
        'why': 'a note declares its voice before its type; new_unpitched_note '
               'had the two reversed (FR15)',
    },
    {
        # add_staff -- TWO schema requirements, neither met. The note's sequence
        # puts <staff> BEFORE <beam>, <notations> and <lyric>, and by the time
        # anything calls this the notations and lyrics are already on the note,
        # so appending puts it last. And <staff> is a POSITIVE INTEGER, but an
        # unresolved `\change Staff = "x"` leaves the CONTEXT IDENTIFIER here
        # and writing that string makes the document unparseable. A note with no
        # <staff> is on staff 1 -- the standard's own default, and the honest
        # answer when the identifier was never resolved.
        'module': 'ly.musicxml.create_musicxml',
        'path': 'ly/musicxml/create_musicxml.py',
        'old': '        staffnode = etree.SubElement(self.current_note, "staff")\n'
               '        staffnode.text = str(staff)',
        'new': '        try:\n'
               '            number = int(staff)\n'
               '        except (TypeError, ValueError):\n'
               '            return\n'
               '        if number < 1:\n'
               '            return\n'
               '        index = -1\n'
               '        for tag in ("beam", "notations", "lyric", "play", "listen"):\n'
               '            i = get_tag_index(self.current_note, tag)\n'
               '            if i != -1 and (index == -1 or i < index):\n'
               '                index = i\n'
               '        staffnode = etree.Element("staff")\n'
               '        staffnode.text = str(number)\n'
               '        if index == -1:\n'
               '            self.current_note.append(staffnode)\n'
               '        else:\n'
               '            self.current_note.insert(index, staffnode)',
        'why': '<staff> comes before beam/notations/lyric in the note sequence '
               'and must be a positive integer; upstream appends it and writes '
               'an unresolved context identifier verbatim (FR15)',
    },
    {
        # add_clef -- the clef's `number` is a staff-number, a positive integer,
        # and the same unresolved context identifier reaches it:
        # `\new Staff = down` produces <clef number="down">.
        'module': 'ly.musicxml.create_musicxml',
        'path': 'ly/musicxml/create_musicxml.py',
        'old': '        if nr:\n'
               '            clefnode = etree.SubElement(self.bar_attr, "clef", number=str(nr))',
        'new': '        try:\n'
               '            nr = int(nr)\n'
               '        except (TypeError, ValueError):\n'
               '            nr = 0\n'
               '        if nr >= 1:\n'
               '            clefnode = etree.SubElement(self.bar_attr, "clef", number=str(nr))',
        'why': "a clef's number attribute is a staff-number and must be a "
               'positive integer, not a context identifier (FR15)',
    },
]



# The order the patched modules must be COMPILED in, and it matters: a module
# that is exec'd binds its imports THERE AND THEN, so a module patched after one
# that imports it would be bound to the unpatched original and its fix would do
# nothing. lymus2musxml does `from . import ly2xml_mediator`, so the mediator has
# to be built first; ly.music.read is imported later still (inside
# Document.__init__) and could go anywhere, but it is listed first because it is
# the deepest.
#
# This bit us: with the list in KNOWN_FIXES order, the calc_trem_dur fix was
# applied, registered, and then ignored, and the oracle recorded output that
# still carried the defect.
MODULE_ORDER = [
    'ly.music.read',
    'ly.musicxml.create_musicxml',
    'ly.musicxml.xml_objs',
    'ly.musicxml.ly2xml_mediator',
    'ly.musicxml.lymus2musxml',
]


def apply_known_fixes():
    """Loads each patched module in place of the reference's own.

    The reference checkout is READ-ONLY: the source is read, the declared
    one-line substitutions are made in memory, and the result is compiled under
    the ORIGINAL file name (so line numbers and tracebacks still point at the
    real file) into a module registered under the real module name.

    Fixes are grouped BY MODULE and applied together. Two fixes in one module
    compiled separately would each start from the untouched source, so the
    second module object would silently replace the first and one of the two
    fixes would be lost -- which is exactly what happened the first time this
    ran with two ly2xml_mediator entries.
    """
    by_module = {}
    for fix in KNOWN_FIXES:
        if fix['module'] not in MODULE_ORDER:
            raise SystemExit(
                'KNOWN_FIXES: {0} is not in MODULE_ORDER. Add it in the '
                'position its imports require.'.format(fix['module']))

        by_module.setdefault((fix['module'], fix['path']), []).append(fix)

    for key in sorted(by_module, key=lambda k: MODULE_ORDER.index(k[0])):
        module_name, rel_path = key
        fixes = by_module[key]
        path = os.path.join(PYTHON_LY, rel_path)
        with open(path, encoding='utf-8') as handle:
            source = handle.read()

        for fix in fixes:
            found = source.count(fix['old'])
            if found != 1:
                raise SystemExit(
                    'KNOWN_FIXES: {0} occurrences of the patched line in {1} '
                    '(expected exactly 1). The reference has moved; re-verify '
                    'the defect before regenerating.\n  line: {2}'.format(
                        found, rel_path, fix['old']))

            source = source.replace(fix['old'], fix['new'])

        module = types.ModuleType(module_name)
        module.__file__ = path
        module.__package__ = module_name.rsplit('.', 1)[0]
        exec(compile(source, path, 'exec'), module.__dict__)
        sys.modules[module_name] = module
        for fix in fixes:
            print('KNOWN FIX applied: {0} -- {1}'.format(module_name, fix['why']))


apply_known_fixes()

import ly.musicxml  # noqa: E402
import ly.musicxml.lymus2musxml as lymus2musxml  # noqa: E402

ENCODING_DATE = re.compile(
    r'(?<=<encoding-date>)[^<]*(?=</encoding-date>)')
SOFTWARE = re.compile(r'(?<=<software>)[^<]*(?=</software>)')


def to_xml(text, **options):
    """Runs python-ly's writer over a document and returns the XML text."""
    writer = ly.musicxml.writer()
    for name, value in options.items():
        setattr(writer, name, value)
    writer.parse_text(text)
    xml = writer.musicxml()
    sio = io.BytesIO()
    xml.write(sio, 'utf-8')
    return sio.getvalue().decode('utf-8')


def normalise(xml):
    """Replaces the two fields that are about the RUN and not about the music."""
    xml = ENCODING_DATE.sub('ENCODING-DATE', xml)
    return SOFTWARE.sub('SOFTWARE', xml)


def harvest(path, out_dir):
    with open(path, encoding='utf-8') as handle:
        text = handle.read().replace('\r', '')

    result = {
        'known_fixes': [
            {'module': f['module'], 'old': f['old'], 'new': f['new'], 'why': f['why']}
            for f in KNOWN_FIXES
        ],
        'reference': 'python-ly v0.9.10',
        'runs': [],
    }

    # The writer's own two switches, both of them recorded, because the port has
    # to answer for each: `midi_out` writes <midi-instrument> blocks from the
    # instrument names, and `language` is what a document with no \language of
    # its own is read in.
    variants = [
        ('default', {}),
        ('midi', {'midi_out': True}),
    ]

    written = 0
    for name, options in variants:
        try:
            xml = normalise(to_xml(text, **options))
            run = {'name': name, 'options': options, 'answered': True, 'xml': xml}
        except Exception as exception:                      # noqa: BLE001
            # An honest oracle records what upstream CANNOT do as well as what
            # it can: a document the reference raises on is a document the port
            # is not required to answer either (traps 48/49's lesson).
            run = {
                'name': name, 'options': options, 'answered': False,
                'error': '{0}: {1}'.format(type(exception).__name__, exception),
            }
        result['runs'].append(run)
        written += 1 if run['answered'] else 0

    base = os.path.splitext(os.path.basename(path))[0]
    with open(os.path.join(out_dir, base + '.musicxml.json'), 'w', encoding='utf-8') as out:
        json.dump(result, out, indent=1, sort_keys=True)
        out.write('\n')
    with open(os.path.join(out_dir, base + '.ly'), 'w', encoding='utf-8') as out:
        out.write(text)

    return written, len(variants)


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    answered = 0
    total = 0
    for path in sys.argv[2:]:
        got, want = harvest(path, out_dir)
        answered += got
        total += want
        print('{0}: {1}/{2} runs answered'.format(os.path.basename(path), got, want))
    print('TOTAL {0}/{1} runs answered over {2} files'.format(
        answered, total, len(sys.argv) - 2))
    print('parser classes exercised: {0}'.format(
        len([n for n in dir(lymus2musxml.ParseSource) if n.startswith('Note')])))


if __name__ == '__main__':
    main()
