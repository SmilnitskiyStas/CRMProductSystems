export interface LegalEntityDto {
  id: string;
  legalName: string;
  edrpou: string | null;
  legalAddress: string | null;
  directorName: string | null;
  phone: string | null;
  email: string | null;
  iban: string | null;
  bankName: string | null;
  isVatPayer: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateLegalEntityDto {
  legalName: string;
  edrpou?: string | null;
  legalAddress?: string | null;
  directorName?: string | null;
  phone?: string | null;
  email?: string | null;
  iban?: string | null;
  bankName?: string | null;
  isVatPayer: boolean;
}

export interface UpdateLegalEntityDto {
  legalName: string;
  edrpou?: string | null;
  legalAddress?: string | null;
  directorName?: string | null;
  phone?: string | null;
  email?: string | null;
  iban?: string | null;
  bankName?: string | null;
  isVatPayer: boolean;
  isActive: boolean;
}
