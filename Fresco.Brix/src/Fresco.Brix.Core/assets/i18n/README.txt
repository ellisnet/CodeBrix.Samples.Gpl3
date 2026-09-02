Interface translations shipped with Fresco.Brix
===============================================

WHAT IS HERE

  Thirteen compiled GNU gettext catalogs, one per interface language, in the
  layout gettext itself expects:

      <language>/LC_MESSAGES/frescobaldi.mo

  cs  Czech          it  Italian         ru  Russian
  de  German         nl  Dutch           sv  Swedish
  es  Spanish        pl  Polish          tr  Turkish
  fr  French         pt_BR  Brazilian    uk  Ukrainian
  gl  Galician              Portuguese

  English is not a catalog: it is what the application says when no catalog is
  installed, and it is the language every msgid is written in.


WHERE THEY COME FROM, AND WHOSE THEY ARE

  They are FRESCOBALDI'S OWN translation catalogs, compiled from the PO files
  in that project's i18n/frescobaldi/ folder. Each one names its translators
  in its header, and those names travel with the file. They are covered by
  Frescobaldi's licence, GPL-2.0-or-later, which this application's
  GPL-3.0-only conveyance accepts; see THIRD-PARTY-NOTICES.txt section 2.3.

  The domain name -- frescobaldi.mo -- is deliberately not renamed. The
  catalogs ARE Frescobaldi's work, and calling them something else would
  obscure that.

  Fresco.Brix keys every user-visible string on the verbatim upstream msgid,
  which is what lets these catalogs translate this application at all. Where a
  ruling made this application say something different -- no menu, button or
  tooltip names LilyPond (ruling FR13), and the application is not Frescobaldi
  (ruling FR9) -- the msgid is Fresco.Brix's own, no catalog has it, and it
  shows in ENGLISH on purpose. Those strings are listed one by one in
  tools/i18nharvest/renamed-strings.tsv.


HOW COMPLETE THEY ARE

  Not very, in some languages, and that is upstream's own state rather than
  anything done here: a translation marked "fuzzy" in the PO file is one the
  translator has not confirmed since the English changed, and GNU msgfmt --
  and therefore this application -- leaves it out. Galician and Turkish are
  most of the way fuzzy and come out largely English; German and Italian are
  complete.

  The figures, per language, are in
  ~/ClaudeHome/STATUS_frescobrix_wi18n_2026-09-01.txt.


REGENERATING THEM

  tools/i18nharvest/harvest.py, which reads the read-only Frescobaldi checkout
  and writes this folder. It ships nothing, it is not in the solution, and no
  build, test or pack step runs it. Its MO writer is checked against GNU
  msgfmt's own output, entry for entry, by
  tools/i18nharvest/gen-i18n-fixtures.py.


DROPPING THEM

  This whole folder can be emptied. The language picker on the General
  preferences page then offers only "No Translation" and "System Default
  Language (if available)", and the application runs in English.
