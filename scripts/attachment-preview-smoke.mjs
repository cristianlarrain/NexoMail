import assert from 'node:assert/strict'
import { buildAttachmentUrl } from '../src/frontend/src/utils/attachmentUrl.ts'

const attachment = {
  id: 'part/2?token=#abc',
  name: 'informe final #1 + revisión.pdf',
  contentType: 'application/pdf',
  size: 42,
}

const preview = buildAttachmentUrl('account/id', 'message/with?query#fragment', attachment)
assert.equal(
  preview,
  '/api/mail/attachments/account%2Fid?messageId=message%2Fwith%3Fquery%23fragment&attachmentId=part%2F2%3Ftoken%3D%23abc&fileName=informe+final+%231+%2B+revisi%C3%B3n.pdf#page=1&view=Fit&zoom=page-fit&navpanes=0&pagemode=none',
)

const download = buildAttachmentUrl('account/id', 'message/with?query#fragment', attachment, true)
assert.equal(
  download,
  '/api/mail/attachments/account%2Fid?messageId=message%2Fwith%3Fquery%23fragment&attachmentId=part%2F2%3Ftoken%3D%23abc&fileName=informe+final+%231+%2B+revisi%C3%B3n.pdf&download=true',
)
assert.equal(download.includes('#page='), false)

console.log('PASS: URL de vista previa y descarga codificada correctamente')
