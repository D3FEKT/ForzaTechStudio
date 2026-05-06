# BXML Editor

The **BXML Editor** page lets you open, inspect, edit, and export **Binary XML** (BXML) files - a compact binary encoding of XML data used extensively throughout ForzaTech for configuration, data tables, and scripting files.

---

## What is BXML?

BXML is a binary-serialized form of XML. Rather than storing human-readable text, the game compiles XML documents into a tightly packed binary representation at build time for faster loading and smaller file size. The BXML Editor decodes this binary form back into a navigable XML tree and allows you to edit values and re-export.

BXML files in Forza often use a `.xml` file extension despite containing binary data - this is normal and expected. The editor detects the format automatically on load.

---

## Opening a File

- **Drag and drop** a BXML or plain XML file onto the page.
- Use the **Open** button in the toolbar.

The editor auto-detects whether the file is binary BXML or plain text XML and loads accordingly.

---

## Page Layout

| Area | Description |
|---|---|
| **Left - XML Tree** | Hierarchical tree of all elements in the document |
| **Right - Node Detail** | Attributes and children summary for the selected node |
| **Bottom - Raw XML tab** | Shows the document as formatted text XML; supports direct editing and Apply |

---

## XML Tree

The tree displays every element in the document. Nodes are expanded to two levels by default; deeper levels can be expanded manually by clicking the arrow next to a node.

Each node shows:

- **Element name** - the XML tag name.
- **Attribute count** - indicated in the detail panel when selected.
- **Child count** - shown in the node detail panel.

---

## Node Detail Panel

When a node is selected the right panel shows:

- All **attributes** for that element in a key/value table.
- The **child element count** (e.g. "3 child elements").

### Editing Attributes

Double-click any attribute value in the table to edit it inline. Changes are reflected in the in-memory document immediately.

If a node has no attributes, a placeholder message indicates this.

---

## Raw XML Tab

The **Raw XML** tab shows the current document serialized as formatted, human-readable XML text. You can:

- **Refresh** - re-render the current in-memory document to text.
- **Edit the text directly** in the text box.
- **Apply** - parse your edited text back into the in-memory document tree.

This is useful for making bulk changes or copying sections of the document to/from other tools.

---

## Saving

| Action | Description |
|---|---|
| **Save** (`Ctrl + S`) | Write back as binary BXML if the file was originally binary, or as text XML if the original was plain text |
| **Save As BXML** | Force export as binary BXML regardless of the original format |
| **Save As XML** | Force export as plain text XML |

> The round-trip encoding preserves all attribute names and values exactly. Element ordering is preserved.

---

## Tips

- When porting BXML files between game versions, open in the BXML Editor, make any version-specific attribute edits, then re-export as BXML.
- The Raw XML tab is the fastest way to find a specific attribute value - use `Ctrl + F` in the text box to search.
- Files exported as plain XML can be edited in any text editor and then re-opened in the BXML Editor to convert back to binary.
