// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Xunit;

// ⚠ THE SUITE RUNS SERIALLY, AND IT HAS TO.
//
// The engraving engine is PROCESS-GLOBAL: one interpreter, one Scheme session, one set of
// `define-session' variables -- and, the part that bites, ONE PROCESS WORKING DIRECTORY.
// Both of the things this suite does drive it. Documentation generation changes directory
// because upstream's generate-documentation.ly writes its nineteen files by RELATIVE name,
// and snippet engraving changes directory because each snippet engraves in its own scratch
// like the sweep gives each file one.
//
// xunit runs separate collections in parallel by default, and a class fixture with no
// collection of its own IS a separate collection. Wave LD1 had exactly one engine-driving
// collection, so this never showed; LD2 added a second, and the two immediately raced --
// the Internals Reference fixture failed with "the source for manual 'internals' is not
// at .../generated/internals.texi", because a snippet render had moved the working
// directory out from under the generation step (measured 2026-08-19, 10 failures).
//
// The failure is worth remembering for its SHAPE: it named a missing generated file, so it
// read as a generation defect rather than as a scheduling one. Nothing about the message
// points at parallelism.
//
// Disabling parallelization assembly-wide is the honest fix rather than putting every test
// in one named collection: the constraint belongs to the ENGINE, so every future test class
// inherits it without having to know the rule exists.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
