# Skill: Create Table View

Components: Table, TableHeader, TableBody, TableRow, TableCell (shadcn/ui)

Pattern:
- Props: data[], onEdit, onDelete, isDeleting
- AlertDialog for delete confirmation (local state: pendingDeleteId)
- Empty state row when data.length === 0
- Action buttons: ghost icon buttons (Pencil, Trash2)

Domain-specific (ShelfGuard):
- Stock quantity colored red when <= reorderLevel
- Status shown as Badge (active/inactive, safe/warning/critical/expired)
- Dates in DD.MM.YYYY format
