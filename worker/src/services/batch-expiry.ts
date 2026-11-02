/** Recheck at UPDATE time: a sensor may place a hold after the job's SELECT. */
export const updateBatchExpirySql = `UPDATE product_stock
 SET "Status" = $1,
     "LastCheckedAt" = $2,
     "NotifiedWarningAt" = CASE WHEN $3 THEN $2::timestamptz ELSE "NotifiedWarningAt" END,
     "NotifiedCriticalAt" = CASE WHEN $4 THEN $2::timestamptz ELSE "NotifiedCriticalAt" END
 WHERE "Id" = $5 AND "Quantity" > 0
   AND "Status" NOT IN ('temp_violation','archived','sold_out')`;
