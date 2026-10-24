// Stand-in for @nestjs/common so a donor domain file that only throws BadRequestException can be loaded without NestJS.
export class BadRequestException extends Error{constructor(message){super(message);this.name='BadRequestException';this.status=400;}}
