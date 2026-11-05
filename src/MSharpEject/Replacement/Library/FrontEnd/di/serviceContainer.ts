import { ServiceDescription } from "./serviceDescription";

export class ServiceContainer {
    private services: { [key: string]: ServiceDescription } = {};

    tryAddSingleton(key: string, factory: (...args: IService[]) => IService, serviceOut: IOutParam<ServiceDescription>): boolean {
        return this.try(key, true, factory, serviceOut);
    }

    tryAddTransient(key: string, factory: (...args: IService[]) => IService, serviceOut: IOutParam<ServiceDescription>): boolean {
        return this.try(key, false, factory, serviceOut);
    }

    addSingleton(key: string, factory: (...args: IService[]) => IService): ServiceDescription {
        return this.add(key, true, factory);
    }

    addTransient(key: string, factory: (...args: IService[]) => IService): ServiceDescription {
        return this.add(key, false, factory);
    }

    getService<T extends IService>(key: string): T {
        const service = this.services[key];
        if (!service) throw new Error(`No service registered for '${key}'.`);
        return service.getInstance() as T;
    }

    private try(key: string, singleton: boolean, factory: (...args: IService[]) => IService, serviceOut: IOutParam<ServiceDescription>): boolean {
        if (this.services[key]) {
            serviceOut.value = this.services[key];
            return false;
        }
        serviceOut.value = this.add(key, singleton, factory);
        return true;
    }

    private add(key: string, singleton: boolean, factory: (...args: IService[]) => IService): ServiceDescription {
        return this.services[key] = new ServiceDescription(key, singleton, this).setFactory(factory);
    }
}
