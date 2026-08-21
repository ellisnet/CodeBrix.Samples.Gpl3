#!/usr/bin/env python3
"""CodeBrix.LilyPort repo tool (ships nothing): records what LilyPond's own
convert-ly does to a corpus of real input files, as the fixtures
ConvertLyParityTests replays.

THE ORACLE IS UPSTREAM'S OWN CODE, RUN HERE. convertrules.py imports nothing but
the standard library and a gettext marker, so there is no shim to write and no
AST lifting to do (board trap 21/46) -- the module is imported and its rules are
called directly. convert-ly.py's driver semantics (which rules run, and what
version is written back) are reproduced in this file rather than imported,
because the script is written around argument parsing and file I/O.

EVERY RULE IS EXERCISED. Each corpus file is converted from 1.2.3 -- before the
first rule -- to the newest version any rule targets, so all 326 rules run over
every file, on text that is real LilyPond rather than a probe made to fit them.
Each file is ALSO converted from the version it actually declares, which is what
a user does and what exercises the version-selection and version-rewriting
logic.

Reads the READ-ONLY LilyPond checkout (standing rule 3); writes
tests/CodeBrix.LilyPort.Tests/fixtures/convertly/.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-convertly-fixtures.py [<lilypond-checkout>]
"""
import builtins
import io
import json
import os
import re
import signal
import sys

CHECKOUT = os.path.expanduser(
    sys.argv[1] if len(sys.argv) > 1 else '~/GitHome/lilypond')
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'tests', 'CodeBrix.LilyPort.Tests', 'fixtures', 'convertly'))

# convertrules.py marks its messages for translation with `_'. Nothing installs a
# catalog here, so the marker is the identity -- which is exactly what the port does.
builtins.__dict__.setdefault('_', lambda text: text)
sys.path.insert(0, os.path.join(CHECKOUT, 'python'))

import convertrules  # noqa: E402

# How many corpus files to record, and how big one may be. The whole regression
# suite would make a 40 MB fixture set that says nothing the sample does not.
SAMPLE = 150
MAX_BYTES = 8192

# ⚠ SOME OF UPSTREAM'S OWN PATTERNS NEVER FINISH. The nested paren_matcher(25)
# alternations in rule 2.15.18 backtrack catastrophically on real input, and python's
# `re' has no timeout -- convert-ly simply hangs. A file the oracle cannot finish is
# therefore SKIPPED and NAMED in the manifest, never silently dropped, and the port's
# own answer for such a file is its match timeout rather than a hang.
ORACLE_TIMEOUT_SECONDS = 20


class OracleTimeout(Exception):
    pass


def _on_alarm(signum, frame):
    raise OracleTimeout()


signal.signal(signal.SIGALRM, _on_alarm)

LOOSE_VERSION_RE = r'\\version *"([0-9.]+)"'
STRICT_VERSION_RE = r'\\version *"([0-9]+)\.([0-9]+)(?:\.([0-9]+))?"'


def guess_version(text):
    """convert-ly.py's guess_lilypond_version, as a tuple or None."""
    m = re.search(STRICT_VERSION_RE, text)
    if m and (m.group(3) is not None or int(m.group(2)) % 2 == 0):
        return (int(m.group(1)), int(m.group(2)), int(m.group(3) or 0))
    return None


def do_conversion(text, from_version, to_version, messages):
    """convert-ly.py:189-225, with stderr captured instead of printed."""
    last_conversion = None
    last_change = None
    applied = []
    errors = 0
    try:
        for version, function, _message in convertrules.conversions:
            if not (from_version < version <= to_version):
                continue
            new_text = function(text)
            last_conversion = version
            applied.append(version)
            if new_text != text:
                last_change = version
            text = new_text
    except convertrules.FatalConversionError:
        errors += 1
    return last_conversion, last_change, text, errors, applied


def convert(text, from_version, to_version):
    """convert-ly.py's do_one_file, minus the file handling."""
    messages = []
    convertrules.stderr_write = messages.append
    # 2.15.18 LEARNS names as it goes and keeps them in a module global; a run over
    # one file must not inherit what another taught it.
    convertrules.should_really_be_music_function = (
        "(?:set-time-signature|empty-music|add-grace-property|"
        "remove-grace-property|set-accidental-style)")

    original = io.StringIO(text, newline=None).read()
    declared = guess_version(original)

    last, last_change, result, errors, applied = do_conversion(
        original, from_version, to_version, messages)

    stamp = last
    if stamp:
        if result == original:
            stamp = declared or from_version
        else:
            stamp = last_change
            if stamp and stamp[1] % 2:
                next_stable = (stamp[0], stamp[1] + 1, 0)
                if next_stable <= to_version:
                    stamp = next_stable

    if stamp:
        new_version = r'\version "%s"' % '.'.join(str(p) for p in stamp)
        if re.search(LOOSE_VERSION_RE, result):
            result = re.sub(LOOSE_VERSION_RE, '\\' + new_version, result)
        else:
            result = new_version + '\n' + result

    return {
        'from': list(from_version),
        'to': list(to_version),
        'last': list(last) if last else None,
        'last_change': list(last_change) if last_change else None,
        'stamp': list(stamp) if stamp else None,
        'applied': [list(v) for v in applied],
        'errors': errors,
        'messages': messages,
        'output': result,
    }


def main():
    os.makedirs(OUT, exist_ok=True)
    latest = convertrules.conversions[-1][0]
    earliest_input = (1, 2, 3)

    source_dir = os.path.join(CHECKOUT, 'input', 'regression')
    names = sorted(n for n in os.listdir(source_dir) if n.endswith('.ly'))
    names = [n for n in names
             if os.path.getsize(os.path.join(source_dir, n)) <= MAX_BYTES]
    step = max(1, len(names) // SAMPLE)
    chosen = names[::step][:SAMPLE]

    cases = []
    skipped = []
    for name in chosen:
        with open(os.path.join(source_dir, name), encoding='utf-8') as handle:
            text = handle.read()

        base = os.path.splitext(name)[0]
        declared = guess_version(text)

        signal.alarm(ORACLE_TIMEOUT_SECONDS)
        try:
            record = {
                'name': base,
                'input': io.StringIO(text, newline=None).read(),
                'declared': list(declared) if declared else None,
                # Everything, from before the first rule -- all 326 run.
                'full': convert(text, earliest_input, latest),
            }
            if declared:
                # And what a user actually gets: from the file's own version.
                record['declared_run'] = convert(text, declared, latest)
        except OracleTimeout:
            skipped.append(base)
            print('SKIPPED (oracle did not finish in %ds): %s'
                  % (ORACLE_TIMEOUT_SECONDS, base))
            continue
        finally:
            signal.alarm(0)

        with open(os.path.join(OUT, base + '.convertly.json'), 'w',
                  encoding='utf-8') as out:
            json.dump(record, out, indent=1, sort_keys=True)
            out.write('\n')
        cases.append(base)

    with open(os.path.join(OUT, 'manifest.json'), 'w', encoding='utf-8') as out:
        json.dump({
            'rules': len(convertrules.conversions),
            'latest': list(latest),
            'earliest_input': list(earliest_input),
            'cases': cases,
            'skipped_oracle_did_not_finish': skipped,
        }, out, indent=1, sort_keys=True)
        out.write('\n')

    print('recorded %d cases (%d skipped); %d rules; latest %s'
          % (len(cases), len(skipped), len(convertrules.conversions),
             '.'.join(str(p) for p in latest)))


if __name__ == '__main__':
    main()
