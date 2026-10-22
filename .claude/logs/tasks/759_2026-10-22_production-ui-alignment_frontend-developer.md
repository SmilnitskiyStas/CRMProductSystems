# TASK-759 Production block UI alignment (style only, no behavior change)

Deviations fixed (reference: /transfers, /receipts, shared ui primitives):
- RecipeTable: hand-rolled add button -> Btn; h1 20->22 + header block; row action icon buttons -> ActionMenu; loading text -> Table isLoading; badges -> pill (radius 20, 3px 8px, 11px). Recipes page: "show inactive" toggle moved into header (toolbarExtra prop).
- ProductionOrderTable: hand-rolled add button -> Btn; filter select restyled to filter-input style, moved to filter row; h1 22; loading via Table; OrderStatusBadge pill.
- ProductionOrderForm / RecipeForm: custom overlay -> Modal; cancel/submit -> Btn (ghost/primary); inputs/labels -> shared form style (#111827, radius 8); add-ingredient -> Btn ghost sm.
- ProductionOrderDetail: removed custom back bar/btnStyle; back link + page padding/gap like OrderCard; status buttons -> Btn (primary/success/danger).
- ProductionRequestsPage: h1 22, column gap 20, tabs like transfers status tabs, filters 7px 12px, Common loading text. Request badges -> pill.
- OrderCard / AutoOrdersSection: hand-rolled <table> -> Table; inputs radius 8/#111827; badges pill; h1 22; gap 20. Print sheet untouched.
Left as is: ProductionRequestForm/Detail/Summary (already Modal/Btn/Table), module-gate lock screens (same on other pages).
Build: tsc --noEmit clean, eslint clean on changed files.
