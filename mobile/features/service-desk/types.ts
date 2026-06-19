export interface Ticket {
  id: string
  number: number
  title: string
  description: string
  category: string     // general | technical | billing | feature_request | bug
  priority: string     // low | medium | high | critical
  status: string       // open | in_progress | waiting | resolved | closed
  createdBy: string
  createdByName: string
  assignedTo: string | null
  assignedToName: string | null
  locationId: string | null
  locationName: string | null
  resolvedAt: string | null
  createdAt: string
  commentCount: number
}

export interface TicketDetail extends Ticket {
  comments: TicketComment[]
}

export interface TicketComment {
  id: string
  authorId: string
  authorName: string
  body: string
  isInternal: boolean
  createdAt: string
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface CreateTicketPayload {
  title: string
  description: string
  category?: string
  priority?: string
  locationId?: string | null
}

export interface UpdateTicketPayload {
  status?: string
  priority?: string
  assignedTo?: string | null
}

export interface AddCommentPayload {
  body: string
  isInternal?: boolean
}
