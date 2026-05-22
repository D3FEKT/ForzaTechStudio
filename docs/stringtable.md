# String Tables

The **String Tables** page lets you open, edit, save, and export Forza `.str` string table files — the binary localization files the game uses to store all in-game text such as car names, race descriptions, and UI labels.

---

## What is a String Table?

A `.str` file is a compact binary file that holds a collection of localized strings for one named category of text. Each entry has three pieces of information:

| Field | Description |
|---|---|
| **Hash ID** | A numeric ID derived from the entry's key name, used by the game to look up strings at runtime |
| **Key Name** | The symbolic name for the string, e.g., `CAR_FORD_MUSTANG_GT500_NAME` |
| **Content** | The actual display text shown in-game, stored as UTF-16 |

String tables are grouped by language and category. For example, all car names for English are in one file, all race names in another.

**File location in the game data:**
```
game:\media\StringTables\<language>\<tableName>.str
```

Examples:
- `game:\media\StringTables\en-US\CarNames.str`
- `game:\media\StringTables\en-US\UIText.str`
- `game:\media\StringTables\fr-FR\RaceNames.str`

---

## Opening a File

- **Drag and drop** a `.str` file onto the page.
- Use the **Open** button (`Ctrl + O`) in the toolbar.
- Use **New** (`Ctrl + N`) to create a blank string table from scratch.

Multiple files can be open at the same time and are accessible via the **Open files** dropdown at the top of the page.

---

## Page Layout

| Area | Description |
|---|---|
| **Toolbar** | New, Open, Save, Save As, Add Entry, Delete, Undo, Redo, Export CSV, Close |
| **Open files dropdown** | Switch between multiple open string tables |
| **Entry list** | All entries in the current file, with sortable Hash ID, Key Name, and Content columns |
| **Search bar** | Filter entries by Hash ID, Key Name, or Content in real time |
| **Entry editor panel** | Edit the Key Name and Content of the selected entry |

---

## Entry List

The entry list shows all strings in the loaded file. Columns can be clicked to sort:

| Column | Description |
|---|---|
| **Hash ID (Hex)** | The 32-bit hash ID in hexadecimal |
| **Hash ID (Dec)** | The same hash ID in decimal |
| **Key Name** | The symbolic key string (e.g., `CAR_BMW_M3_NAME`) |
| **Content** | The localized display text (e.g., `BMW M3`) |

Use the **search bar** to filter entries by any of these fields.

Right-clicking an entry opens a context menu with options to copy the Hash ID, Key Name, or Content individually, or copy and paste the entire row.

---

## Editing Entries

Select any entry in the list to open it in the right-side editor panel. From there you can:

- Edit the **Key Name** — the hash ID is automatically recalculated when you change the key.
- Edit the **Content** — the display text shown in-game.

Changes are applied immediately to the in-memory table. Use **Undo** (`Ctrl + Z`) and **Redo** (`Ctrl + Y`) to step through your edit history.

---

## Adding and Removing Entries

- **Add Entry** — creates a new blank entry. Fill in the Key Name and Content in the editor panel. The hash ID is computed automatically from the key name.
- **Delete** — removes the selected entry or entries from the table.

---

## Saving

| Action | Shortcut | Description |
|---|---|---|
| **Save** | `Ctrl + S` | Write changes back to the original file |
| **Save As** | `Ctrl + Shift + S` | Save to a new location |

The file is always written in the native Forza `.str` binary format.

---

## Export CSV

Use **Export CSV** to save all entries as a comma-separated values file. The CSV contains columns for Hash ID (hex), Hash ID (decimal), Key Name, and Content. This is useful for diffing string tables between game versions or bulk editing in a spreadsheet.

---

## File Format Overview

`.str` files use a simple binary layout. Two format versions are supported:

### Version `0x0400` 

- A fixed 140-byte **file header** that stores the format version (`0x0400`) and the table name (the filename without extension, e.g., `CarNames`).
- A **content section** — an array of hash/offset pairs followed by a packed block of UTF-16 LE strings containing the display text for each entry.
- A **names section** — the same structure but storing the symbolic key name strings. This section may be absent in stripped builds.

All strings are UTF-16 little-endian.

### Version `0x0800` (Forza Horizon 6)

- A variable-length **file header** containing the format version (`0x0800`), the table name (126 bytes), and an array of absolute offsets to each table block.
- Each **table block** contains a size field, a string-data size field, an array of hash/offset pairs, and a packed block of null-terminated UTF-8 strings.
- Table 0 holds the localized display values; Table 1 (if present) holds the symbolic `IDS_` key names.

All strings are UTF-8 (null-terminated single-byte).

The file uses no compression or encryption in either version.

---

## Tips

- The Key Name is case-sensitive and must be ASCII. The hash is recalculated automatically whenever you edit it, so you do not need to manage hash IDs manually.
- If a file was extracted from an archive that has no names section, the Key Name column will be blank. You can still edit Content freely.
- Use Export CSV when porting a string table to another language — translate the Content column in the spreadsheet, then paste back using copy/paste row operations.
- To compare two versions of the same string table side by side, open both files and switch between them using the Open files dropdown.




