export type ReceiptLine = {
  id:string;productId:string;productName:string;productBarcode:string|null;unit:string|null;quantityOrdered:string;
  quantityReceived:string|null;pricePurchase:string|null;expiryDate:string|null;batchNumber:string|null;discrepancyNotes:string|null;
};

export type ReceiptSource = {
  id:string;destinationStoreId:string;destinationStoreName:string;supplierId:string|null;supplierName:string|null;
  status:string;viaCentralStore:boolean;expectedAt:string|null;notes:string|null;source:string;takenByUserId:string|null;
  receivingFingerprint:string;items:ReceiptLine[];receivedAt:string|null;
  erpDocumentId:string|null;erpPostingId:string|null;erpReplayed:boolean;receivingWork?:ReceiptWork;
};
export type ReceiptWorkGroup={key:string;name:string;lineIds:string[];ownerId:string|null;ownerName:string|null;fingerprint:string;ready:boolean;completedAt:string|null};
export type ReceiptWork={receiptId:string;locationId:string;active:boolean;open:boolean;groups:ReceiptWorkGroup[]};
export type ReceiptWorkCommand={action:'start'|'take'|'release'|'reopen'|'save';key?:string;lineIds?:string[];save?:{requestId:string;expectedFingerprint:string;items:ReceiptLineEdit[];complete:boolean}};

export type ReceiptLineEdit={itemId:string;quantityReceived:string;pricePurchase:string|null;expiryDate:string;batchNumber:string;discrepancyNotes:string|null};
export type User={id:string;role:string;twoFactorEnabled?:boolean};
export type Result<T>=T;
