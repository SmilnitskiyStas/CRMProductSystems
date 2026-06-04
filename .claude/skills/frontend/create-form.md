# Skill: Create Form

Libraries: react-hook-form + zod + @hookform/resolvers

Pattern:
1. Define zod schema
2. useForm with zodResolver
3. FormField + FormControl + FormMessage (shadcn/ui Form)
4. onSubmit calls onCreate or onUpdate prop
5. Reset form on dialog open via useEffect

Rules:
- SKU disabled when editing (immutable after create)
- isPending disables submit button
- Errors shown inline via FormMessage
