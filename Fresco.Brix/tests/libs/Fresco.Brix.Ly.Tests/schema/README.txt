The official MusicXML W3C XML Schema, vendored so the conformance tests can run
offline and so that "conformant" means conformant to the PUBLISHED schema rather
than to anything this repository believes about it.

  musicxml.xsd   MusicXML 4.0 W3C XML Schema (XSD)
  xlink.xsd      the XLink schema it imports
  xml.xsd        the xml: namespace schema it imports

WHERE THEY CAME FROM, exactly, so a future developer can refresh them:

  https://github.com/w3c/musicxml   tag v4.0   directory schema/

  Fetched 2026-08-24. Copied VERBATIM — do not edit them. The two xs:import
  statements inside musicxml.xsd point at
  http://www.musicxml.org/xsd/xml.xsd and .../xlink.xsd, which is why the
  specification also ships a catalog.xml; MusicXmlSchemaTests resolves those two
  names to the local files instead, and does NOT rewrite the file to do it.

WHO MAINTAINS THEM

  MusicXML is an open standard, not an invention of Frescobaldi, of python-ly or
  of this project. It was created by Recordare LLC (Michael Good), its copyright
  passed to MakeMusic in 2011, and since 2017 it has been developed by the
  W3C MUSIC NOTATION COMMUNITY GROUP:

      https://www.w3.org/community/music-notation/

  Version 4.0 (June 2021) is the current published version, a W3C Community
  Group Final Report. The reference documentation is at:

      https://www.w3.org/2021/06/musicxml40/

  Version 4.1 is in draft on the group's `master` branch.

LICENCE

  Copyright (c) 2004-2021 the Contributors to the MusicXML Specification,
  published by the W3C Music Notation Community Group under the
  W3C Community Final Specification Agreement (FSA):

      https://www.w3.org/community/about/agreements/final/

  These files are TEST resources. They are not compiled into Fresco.Brix, are
  not copied to any head's output, and are not conveyed with the application —
  they are read by the test process to validate what the exporter writes. An
  entry is owed in THIRD-PARTY-NOTICES.txt all the same.

WHY THEY ARE HERE — RULING FR15

  Fresco.Brix will not write a MusicXML file that does not conform to the
  published schema. That is a hard rule, and a rule nothing enforces is a wish:
  MusicXmlSchemaTests exports every document in the parity corpus and validates
  the result against these files. If the exporter ever emits something the
  schema forbids, that test fails and says which element.
