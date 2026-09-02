=== General Preferences ===

Under *General* preferences, you can choose in which language {appname}'s
user interface is translated.

You can also choose whether a file opened from elsewhere — a file manager, or a
second launch of the program — is opened in the window that is already running,
or in a new one.

Under *Session to load if {appname} is started without arguments* you can 
configure which session to load if {appname} is started without a 
filename. You can choose whether to start with one empty document, with the 
last used session, or with a specific session. Please note that this only 
works when you have explicitly created a session and set it to automatically 
add files on save to it. See also {sessions}.

Under *When saving documents*, you can choose what to do when a document is 
saved, such as remembering the cursor position and marked lines, formatting,
or leaving a backup copy of the document (with a `~` appended).

Also, you can specify a default folder in which you keep your LilyPond 
documents.

Under *Creating new documents*, you can choose what to do when a new document
is created. It can be left empty (the default), it can be given the version
statement the built-in engraver is compatible with, or you can choose any of
the templates you defined.

Under *Experimental Features*, you can choose whether to enable features that
are in development and are not yet considered complete.
See {experimental}.

#VARS
sessions help sessions
experimental help experimental_features
