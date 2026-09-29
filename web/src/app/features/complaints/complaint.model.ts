export type ComplaintStatus = 'open' | 'in_progress' | 'resolved';

export interface Complaint {
  id: string;
  programId: string | null;
  customerKey: string | null;
  subject: string;
  description: string | null;
  status: ComplaintStatus;
  createdAt: string;
  resolvedAt: string | null;
}

export interface CreateComplaintRequest {
  programId?: string | null;
  customerKey?: string | null;
  subject: string;
  description?: string | null;
}
