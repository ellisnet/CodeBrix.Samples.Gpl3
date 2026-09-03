fixtures/icons -- ruling FR14's recorded original
=================================================

tools-score-wizard.{light,dark}.upstream.svg.gz are Frescobaldi's
OWN tools-score-wizard.svg from commit cec205b9, byte for byte,
gzip-compressed. They are 6,474,769 and 6,474,783 bytes
uncompressed: 228,519 lines each, of which 20,760 are orphan
<inkscape:path-effect> elements inside <defs>, for a 24-pixel icon.

The application ships a CLEANED copy of each (ruling FR14; see
src/Fresco.Brix.Core/assets/icons/README-frescobaldi-icons.txt).
IconThemeTests renders the recorded original and the shipped file
through the application's own renderer at 24, 48 and 96 pixels and
asserts that every pixel agrees -- which is what makes the clean a
size fix and not a redraw.

Recorded by tools/iconclean/iconclean.py, which ships nothing and
is not in the solution. The fixture is here, and not read from the
Frescobaldi checkout at test time, because no build or test step
may name that checkout's path.
