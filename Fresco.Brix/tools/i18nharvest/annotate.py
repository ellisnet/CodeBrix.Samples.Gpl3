#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""
One-shot annotation of renamed-strings.tsv.

`harvest.py --seed-table' can tell FR13 and FR9 apart on its own, because those
two renames are a substitution it can undo. It cannot tell a sentence a RULING
rewrote from a sentence that was never upstream's at all, and it cannot know
which Qt control a PLATFORM substitution replaced. This script carries those
judgements -- each one read off the wave STATUS files that recorded the change
when it was made -- and applies them to the seeded table.

It is kept beside the table so the judgements are re-appliable and auditable,
not because it runs on every harvest. Run it after a re-seed:

    python3 harvest.py --seed-table && python3 annotate.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import renamed


# msgid -> (category, upstream origin or None)
JUDGEMENTS = {
    # --- ruling FR13: the sentence was rewritten because it named LilyPond ---
    'Show and select the text and music fonts available to LilyPort': (
        'FR13',
        'Show and select text and music fonts available in the LilyPond '
        'version of the current document'),
    'Install fonts from a directory into the folder LilyPort searches': (
        'FR13', 'Link fonts from a directory to the current LilyPond installation'),
    'Install fonts from the global music font repository\n'
    'into the folder LilyPort searches.': (
        'FR13', 'Link fonts from the global music font repository\n'
                'to the current LilyPond installation.'),
    'Where LilyPort looks for fonts:': ('FR13', 'Fontconfig data:'),
    'Generic import for all import formats.': (
        'FR13', 'Generic import for all LilyPond tools.'),
    'Music fonts are installed into {folder}, which LilyPort searches before '
    'its own built-in fonts. Nothing is installed there until you ask for it '
    'in Tools ▸ Document Fonts.': ('FR13', None),
    'No music fonts are installed. {appname} always has emmentaler, which is '
    'built into LilyPort; installing a font here adds it to what LilyPort can '
    'find.': ('FR13', None),
    'Engine: LilyPort {version} (compatible with {compatible}), in this '
    'process.': ('FR13', None),
    'LilyPort {version} (compatible with {compatible}) [{document}]': (
        'FR13', 'LilyPond {version} [{document}]'),
    'the LilyPort engine failed to load': ('FR13', None),
    'loading the LilyPort engine...': ('FR13', None),

    # --- ruling FR9: the application is not Frescobaldi ---
    '{appname} only removes music fonts it installed into its own font '
    'folder. This family includes files from elsewhere and is left alone.': (
        'FR9',
        'To avoid persistent damage Frescobaldi only supports removing music '
        'fonts that are linked into a LilyPond installation.'),
    'If checked, files will be opened in a running {appname} \napplication if '
    'available, instead of starting a new instance.': (
        'FR9',
        'If checked, files will be opened in a running Frescobaldi '
        'application if available, instead of starting a new instance.'),
    'A relative folder is looked for under /usr and /usr/local. The '
    'dictionaries that came with {appname} are always searched as well.': (
        'FR9', None),
    '{appname} User Guide': ('FR9', 'Frescobaldi Manual'),
    '{appname} is written and maintained by {author}.': (
        'FR9', 'Frescobaldi is written and maintained by {author}.'),

    # --- a ruling removed or changed what the sentence described -----------
    'If enabled, line numbers are shown in exported HTML.': (
        'RULING',
        'If enabled, line numbers are shown in exported HTML or printed '
        'source.'),
    'If checked, always assume that the first pitch of a \\relative {...}\n'
    'expression without startpitch is absolute. Otherwise, Fresco.Brix\nonly '
    "assumes this when the document's version is >= 2.18.": (
        'RULING',
        'If checked, always assume that the first pitch of a \\relative '
        '{...}\nexpression without startpitch is absolute. Otherwise, '
        'Frescobaldi\nonly assumes this when the LilyPond version is >= '
        '2.18.'),
    'Fresco.Brix engraves in this process. There is no external LilyPond '
    'installation, and no version to choose.': ('RULING', None),
    'Output: SVG pages, and MIDI where a score asks for it.': ('RULING', None),
    'These options are not applied by this engine yet: {options}': (
        'RULING', None),
    'The file {filename} could not be converted.': (
        'RULING', 'The file {filename} could not be converted: wrong file type.'),

    # --- a Qt control this application does not have was substituted ------
    'Below you can enter commands to open different file types. $f is '
    'replaced with the filename, $u with the URL. Leave a field empty to use '
    'the operating system default application.': (
        'PLATFORM',
        'Below you can enter commands to open different file types. '
        '<code>$f</code> is replaced with the filename, <code>$u</code> with '
        'the URL. Leave a field empty to use the operating system default '
        'application.'),
    'The document "{name}" has been modified.\nDo you want to discard your '
    'changes?': (
        'PLATFORM',
        'The document "{name}" has been modified.\nDo you want to save your '
        'changes or discard them?'),
    "Can't write to destination:": (
        'PLATFORM', "Can't write to destination:\n\n{url}\n\n{error}"),
    'Could not open {filename}:\n{error}': (
        'PLATFORM', 'Could not read from: {url}'),
    'Could not read from {filename}:\n{error}': (
        'PLATFORM', 'Could not read from: {url}'),
    'Could not write to {filename}:\n{error}': (
        'PLATFORM', 'Could not write to: {url}'),
    'Scaling:': ('PLATFORM', None),
    'Fixed scale': ('PLATFORM', 'Fixed scale:'),
    'New document:': ('PLATFORM', None),
    'Template:': ('PLATFORM', None),
    'Session:': ('PLATFORM', None),
    'Instrument:': ('PLATFORM', 'Instrument'),
    'Volume:': ('PLATFORM', None),
    'Volume': ('PLATFORM', None),
    'Red:': ('PLATFORM', None),
    'Green:': ('PLATFORM', None),
    'Blue:': ('PLATFORM', None),
    'Opacity:': ('PLATFORM', None),
    'HTML:': ('PLATFORM', None),
    'Close documents in this folder ({folder})': (
        'PLATFORM', 'Close documents in this folder'),
    'Save documents in this folder ({folder})': (
        'PLATFORM', 'Save documents in this folder'),
    'Configure Keyboard Shortcut': (
        'PLATFORM', 'Configure Keyboard Shortcut ({key})'),
    'Please enter the shortcut, for example Ctrl+Shift+S:': ('PLATFORM', None),
    'Please enter a name for the session:': ('PLATFORM', None),
    '&Search path:': ('PLATFORM', 'Use session specific include path'),
    '&Text:': ('PLATFORM', None),
    'Add Snippet': ('PLATFORM', 'Edit Snippet'),
    'Export &PDF...': ('PLATFORM', 'E&xport...'),
    'Export PN&G...': ('PLATFORM', 'E&xport...'),
    'Export S&VG...': ('PLATFORM', 'E&xport...'),
    'Writes the whole score to a PDF file.': ('PLATFORM', None),
    'Writes the current page to a picture file.': ('PLATFORM', None),
    'Writes the current page to an SVG file.': ('PLATFORM', None),
    # upstream re-translates every open widget at once (app.translateUI);
    # a CodeBrix.Platform window builds its captions once, so the General
    # page says out loud that the change lands at the next launch.
    'A change of language takes effect when {appname} is started again.': (
        'PLATFORM', None),
}

# The action-collection titles. Upstream's ActionCollection.title() answers
# None for all but a handful, and its shortcut page shows an untitled
# collection's actions loose; this port's page titles every collection, so
# these eleven headings have no upstream original and never will.
COLLECTION_TITLES = [
    'Main Window', 'Views', 'Tool Panels', 'Document Actions', 'Editor Margin',
    'Score Wizard', 'Bookmarks', 'Engraving', 'Documentation',
    'Editor Commands', 'Import', 'Automatic Completion', 'Lyrics', 'Rhythm',
    'Rest',
]


def main():
    rows = renamed.load()
    if not rows:
        raise SystemExit('renamed-strings.tsv is empty; seed it first')

    changed = 0
    for row in rows:
        if row.msgid in JUDGEMENTS:
            category, origin = JUDGEMENTS[row.msgid]
            if (row.category, row.origin) != (category, origin):
                row.category, row.origin = category, origin
                changed += 1
        elif row.msgid in COLLECTION_TITLES and row.context is None:
            if row.category != 'PLATFORM':
                row.category, row.origin = 'PLATFORM', None
                changed += 1

    renamed.save(rows)

    missing = [m for m in JUDGEMENTS if m not in {r.msgid for r in rows}]
    for msgid in missing:
        print('!! judgement no longer matches any row:', repr(msgid[:70]))

    print(f'annotated {changed} of {len(rows)} rows')
    counts = {}
    for row in rows:
        counts[row.category] = counts.get(row.category, 0) + 1
    for category in sorted(counts):
        print(f'  {category:<10}{counts[category]:4d}   {renamed.CATEGORIES[category]}')
    return 1 if missing else 0


if __name__ == '__main__':
    raise SystemExit(main())
