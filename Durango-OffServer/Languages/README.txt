Durango: Wild Lands (KOR-V2) - Translation Folders
====================================================

HOW THIS WORKS
--------------
Each folder here (th_TH, en_US, id_ID, ...) is one in-game language.
The game checks this folder FIRST when it loads text for a language;
if a "messages.po" file exists here, the game uses it instead of its
own built-in text. If you don't touch a folder, that language behaves
exactly like the unmodified game.

HOW TO TRANSLATE A LANGUAGE
----------------------------
1. Open the folder for the language you want to edit (e.g. th_TH),
   or copy an existing folder (e.g. en_US) to start a new translation.
2. Open messages.po in Notepad, or better, in the free tool "Poedit"
   (https://poedit.net) which shows original/translation side by side
   and handles special formatting for you.
3. Each entry looks like this:

     msgid "Original Korean/source text"
     msgstr "Your translated text"

   Only edit the text inside the quotes after msgstr. Never edit the
   msgid line - it's the lookup key the game uses to find this entry.
4. Some entries have a line like:

     msgctxt "some_context"

   right before msgid. Leave that line alone too - it just tells the
   game which specific instance of a repeated phrase this is.
5. Watch for special markers inside the text and keep them exactly as
   they appear, just move them to the right place in your translation:
     - Tags like <em>...</em>, <li>...</li>, [size=8][/size]
     - Placeholders like {0}, {1}
     - Escaped codes like \n (line break) and \" (quote mark)
6. Save the file as plain UTF-8 text (Notepad's default "UTF-8" encoding
   is fine; Poedit handles this automatically).
7. Restart the game to see your changes in-game.

FOLDER NAMES
------------
Folder names must exactly match the game's internal language codes:
  th_TH = Thai            en_US = English (US)      id_ID = Indonesian
  ko_KR = Korean          es_MX = Spanish (Mexico)   pt_BR = Portuguese (Brazil)
  ru_RU = Russian         de_DE = German             fr_FR = French
  zh_TW = Chinese (Traditional)

Only th_TH, en_US, and id_ID are provided as a starting point right now.
To add one of the other codes above, create a new folder with that exact
name and copy an existing messages.po into it as your starting point.

NOTES
-----
- en_US's messages.po is a good reference for "what the English text
  currently says", useful when translating into a language you're more
  comfortable comparing against English rather than Korean.
- If a messages.po file has a mistake that breaks its format, the game
  safely falls back to its original built-in text for that language -
  it will not crash, but your edits won't show up until the file is
  fixed.
