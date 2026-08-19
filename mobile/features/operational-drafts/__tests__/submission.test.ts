import { classifyOperationalError } from '../submission';

it('classifies stock conflict without implementing FEFO', () => {
  expect(classifyOperationalError({ response: { status: 409, data: {} } }).status).toBe('conflict');
});

it('classifies timeout as uncertain and blocks blind retry', () => {
  const result = classifyOperationalError({ code: 'ETIMEDOUT' });
  expect(result.status).toBe('uncertain');
  expect(result.message).toContain('Не повторюйте');
});

it('classifies a confirmed rejection as retryable failed state', () => {
  expect(classifyOperationalError({ response: { status: 400, data: {} } }).status).toBe('failed');
});
