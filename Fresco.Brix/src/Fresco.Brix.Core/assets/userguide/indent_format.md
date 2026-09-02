=== Indentation and Formatting ===

By default, {appname} will automatically indent two spaces after
characters such as `{` and `<<`. This is in accordance with the indenting
the LilyPond documentation uses.

You can change the indenting behaviour by using [docvars document variables].
In the following example, {appname} will use 4 spaces for indent.

```lilypond
% -*- indent-width: 4;
\relative {
    c2 d4 e8 f16 r
}
```

You can also change the default behaviour of {appname} in the
[prefs_editor editor preferences].

Besides indenting, {appname} is also able to align indented lines with 
other characters on the previous line, after the character that starts the
indent. Consider the following example:

```lilypond
\relative {
  << { c d e f g }
     { e f g a b } >>
  d2.
}
```

The line {example} aligns itself with the preceding construct,
regardless of the indent-with currently in use.


#VARS
example md `{ e f g a b }`

